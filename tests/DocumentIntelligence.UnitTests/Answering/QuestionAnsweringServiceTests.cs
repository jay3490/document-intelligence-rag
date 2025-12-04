using DocumentIntelligence.Core.Answering;
using DocumentIntelligence.Core.Search;
using DocumentIntelligence.Core.Storage;
using DocumentIntelligence.Core.Tenancy;
using DocumentIntelligence.Infrastructure.Offline;
using DocumentIntelligence.Infrastructure.VectorStores;
using DocumentIntelligence.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.UnitTests.Answering;

public sealed class QuestionAnsweringServiceTests
{
    private const int Dimensions = 64;

    private static readonly SearchFilter DefaultTenant = new(TenantId.Default);

    [Fact]
    public async Task Returns_only_the_sources_the_answer_cites()
    {
        var chat = new RecordingChatClient("Employees get 25 days [2].");
        var service = await CreateServiceAsync(chat,
            Chunks.Create("Vacation is 25 days per year.", name: "leave.md"),
            Chunks.Create("Vacation requests need manager approval.", name: "process.md"));

        var result = await service.AnswerAsync("How many vacation days?", topK: 2, DefaultTenant, CancellationToken.None);

        Assert.Equal("Employees get 25 days [2].", result.Answer);
        var citation = Assert.Single(result.Citations);
        Assert.Equal(2, citation.Number);

        var prompt = Assert.Single(chat.Calls)[1].Content;
        Assert.True(GroundedPrompt.TryParse(prompt, out var parsed));
        Assert.Equal(parsed.Sources[1].Text, citation.Text);
    }

    [Fact]
    public async Task Does_not_call_the_model_when_nothing_is_indexed()
    {
        var chat = new RecordingChatClient("unused");
        var service = await CreateServiceAsync(chat);

        var result = await service.AnswerAsync("Anything?", topK: null, DefaultTenant, CancellationToken.None);

        Assert.Equal(QuestionAnsweringService.NothingIndexedAnswer, result.Answer);
        Assert.Empty(result.Citations);
        Assert.Empty(chat.Calls);
    }

    [Fact]
    public async Task Uses_the_default_top_k_when_none_is_given()
    {
        var chat = new RecordingChatClient("ok");
        var chunks = Enumerable.Range(0, 6).Select(i => Chunks.Create($"budget line {i}", index: i)).ToArray();
        var service = await CreateServiceAsync(chat, chunks);

        await service.AnswerAsync("budget", topK: null, DefaultTenant, CancellationToken.None);

        Assert.True(GroundedPrompt.TryParse(chat.Calls[0][1].Content, out var parsed));
        Assert.Equal(3, parsed.Sources.Count);
    }

    [Fact]
    public async Task Works_end_to_end_with_the_offline_providers()
    {
        var embeddings = new HashingEmbeddingClient(Dimensions);
        var store = new InMemoryVectorStore(Dimensions);
        var keywordIndex = new Bm25Index();
        var chunks = new[]
        {
            Chunks.Create("The office opens at 8am on weekdays.", name: "office.md"),
            Chunks.Create("Parking permits are issued by facilities.", name: "parking.md"),
        };
        await IndexAsync(store, keywordIndex, embeddings, chunks);
        var service = new QuestionAnsweringService(
            new HybridRetriever(embeddings, store, keywordIndex, Options.Create(new RetrievalOptions()), NullLogger<HybridRetriever>.Instance),
            new ExtractiveChatClient(),
            Options.Create(new RetrievalOptions()),
            NullLogger<QuestionAnsweringService>.Instance);

        var result = await service.AnswerAsync("Who issues parking permits?", topK: 2, DefaultTenant, CancellationToken.None);

        Assert.StartsWith("Parking permits are issued by facilities.", result.Answer);
        Assert.Equal("parking.md", Assert.Single(result.Citations).DocumentName);
    }

    private static async Task<QuestionAnsweringService> CreateServiceAsync(RecordingChatClient chat, params Core.Documents.DocumentChunk[] chunks)
    {
        var embeddings = new HashingEmbeddingClient(Dimensions);
        var store = new InMemoryVectorStore(Dimensions);
        var keywordIndex = new Bm25Index();
        await IndexAsync(store, keywordIndex, embeddings, chunks);

        var options = Options.Create(new RetrievalOptions { DefaultTopK = 3 });
        return new QuestionAnsweringService(
            new HybridRetriever(embeddings, store, keywordIndex, options, NullLogger<HybridRetriever>.Instance),
            chat,
            options,
            NullLogger<QuestionAnsweringService>.Instance);
    }

    private static async Task IndexAsync(InMemoryVectorStore store, Bm25Index keywordIndex, HashingEmbeddingClient embeddings, Core.Documents.DocumentChunk[] chunks)
    {
        var vectors = await embeddings.EmbedAsync(chunks.Select(c => c.SearchText).ToList(), CancellationToken.None);
        await store.UpsertAsync(chunks.Zip(vectors, (c, v) => new EmbeddedChunk(c, v)).ToList(), CancellationToken.None);
        keywordIndex.Add(chunks);
    }
}
