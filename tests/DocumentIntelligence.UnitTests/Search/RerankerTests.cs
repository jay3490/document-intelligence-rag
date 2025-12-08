using DocumentIntelligence.Core;
using DocumentIntelligence.Core.Answering;
using DocumentIntelligence.Core.Chat;
using DocumentIntelligence.Core.Search;
using DocumentIntelligence.Core.Storage;
using DocumentIntelligence.Core.Tenancy;
using DocumentIntelligence.Infrastructure.Offline;
using DocumentIntelligence.Infrastructure.VectorStores;
using DocumentIntelligence.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.UnitTests.Search;

public sealed class RerankerTests
{
    private static readonly RetrievedChunk[] Candidates =
    [
        Retrieved("first passage", 0),
        Retrieved("second passage", 1),
        Retrieved("third passage", 2),
    ];

    [Fact]
    public async Task The_no_op_reranker_keeps_order_and_trims_to_top_k()
    {
        var result = await new NoOpReranker().RerankAsync("q", Candidates, 2, CancellationToken.None);

        Assert.Equal(Candidates.Take(2), result);
    }

    [Fact]
    public async Task Orders_candidates_by_the_model_grades()
    {
        var chat = new RecordingChatClient("1: 2\n2: 9\n3: 5");
        var reranker = new LlmReranker(chat, NullLogger<LlmReranker>.Instance);

        var result = await reranker.RerankAsync("which passage?", Candidates, 2, CancellationToken.None);

        Assert.Equal(new[] { Candidates[1].Chunk.Id, Candidates[2].Chunk.Id }, result.Select(r => r.Chunk.Id));
        Assert.Equal(new double?[] { 9, 5 }, result.Select(r => r.RerankScore));
        var prompt = Assert.Single(chat.Calls);
        Assert.Equal(LlmReranker.SystemInstructions, prompt[0].Content);
        Assert.Contains("<passage id=\"3\">\nthird passage\n</passage>", prompt[1].Content);
        Assert.EndsWith("<question>\nwhich passage?\n</question>", prompt[1].Content);
    }

    [Fact]
    public async Task Ungraded_candidates_follow_graded_ones_in_their_original_order()
    {
        var reranker = new LlmReranker(new RecordingChatClient("3: 4"), NullLogger<LlmReranker>.Instance);

        var result = await reranker.RerankAsync("q", Candidates, 3, CancellationToken.None);

        Assert.Equal(new[] { Candidates[2].Chunk.Id, Candidates[0].Chunk.Id, Candidates[1].Chunk.Id }, result.Select(r => r.Chunk.Id));
        Assert.Null(result[1].RerankScore);
    }

    [Fact]
    public async Task Falls_back_to_the_fused_order_when_the_model_fails()
    {
        var reranker = new LlmReranker(new ThrowingChatClient(), NullLogger<LlmReranker>.Instance);

        var result = await reranker.RerankAsync("q", Candidates, 2, CancellationToken.None);

        Assert.Equal(Candidates.Take(2), result);
    }

    [Fact]
    public async Task Falls_back_when_the_reply_has_no_grades()
    {
        var reranker = new LlmReranker(new RecordingChatClient("I cannot grade these."), NullLogger<LlmReranker>.Instance);

        Assert.Equal(Candidates.Take(3), await reranker.RerankAsync("q", Candidates, 3, CancellationToken.None));
    }

    [Theory]
    [InlineData("1: 7\n2: 3", 2)]
    [InlineData("[1] - 7\n 2 = 3.5", 2)]
    [InlineData("1: 7\n1: 2\n9: 10\n2: 11", 1)]
    public void Parses_grade_lines_and_ignores_out_of_range_entries(string reply, int expected)
    {
        var grades = LlmReranker.ParseGrades(reply, count: 3);

        Assert.Equal(expected, grades.Count);
        Assert.Equal(7, grades[1]);
    }

    [Fact]
    public void Long_passages_are_truncated_in_the_prompt()
    {
        var prompt = LlmReranker.BuildPrompt("q", [Retrieved(new string('x', 5000), 0)]);

        Assert.True(prompt[1].Content.Length < LlmReranker.MaxPassageCharacters + 100);
    }

    [Fact]
    public async Task The_answering_service_reranks_a_wider_candidate_pool()
    {
        var embeddings = new HashingEmbeddingClient(64);
        var store = new InMemoryVectorStore(64);
        var keywordIndex = new Bm25Index();
        var chunks = Enumerable.Range(0, 6).Select(i => Chunks.Create($"budget line {i}", index: i)).ToArray();
        var vectors = await embeddings.EmbedAsync(chunks.Select(c => c.SearchText).ToList(), CancellationToken.None);
        await store.UpsertAsync(chunks.Zip(vectors, (c, v) => new EmbeddedChunk(c, v)).ToList(), CancellationToken.None);
        keywordIndex.Add(chunks);

        var options = Options.Create(new RetrievalOptions { DefaultTopK = 2, Reranker = RerankerKind.Llm, RerankCandidates = 5 });
        var reranker = new CapturingReranker();
        var service = new QuestionAnsweringService(
            new HybridRetriever(embeddings, store, keywordIndex, options, NullLogger<HybridRetriever>.Instance),
            reranker,
            new RecordingChatClient("ok"),
            options,
            NullLogger<QuestionAnsweringService>.Instance);

        await service.AnswerAsync("budget", topK: null, new SearchFilter(TenantId.Default), CancellationToken.None);

        Assert.Equal(5, reranker.CandidateCount);
        Assert.Equal(2, reranker.TopK);
    }

    private static RetrievedChunk Retrieved(string text, int index) =>
        new(Chunks.Create(text, index: index), 1.0 / (index + 1), index + 1, null);

    private sealed class ThrowingChatClient : IChatCompletionClient
    {
        public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken) =>
            throw new UpstreamServiceException("chat", "unavailable");
    }

    private sealed class CapturingReranker : IReranker
    {
        public int CandidateCount { get; private set; }

        public int TopK { get; private set; }

        public Task<IReadOnlyList<RetrievedChunk>> RerankAsync(string query, IReadOnlyList<RetrievedChunk> candidates, int topK, CancellationToken cancellationToken)
        {
            CandidateCount = candidates.Count;
            TopK = topK;
            return Task.FromResult<IReadOnlyList<RetrievedChunk>>(candidates.Take(topK).ToList());
        }
    }
}
