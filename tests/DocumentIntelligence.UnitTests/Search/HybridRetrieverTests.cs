using DocumentIntelligence.Core.Documents;
using DocumentIntelligence.Core.Embeddings;
using DocumentIntelligence.Core.Search;
using DocumentIntelligence.Core.Storage;
using DocumentIntelligence.Core.Tenancy;
using DocumentIntelligence.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.UnitTests.Search;

public sealed class HybridRetrieverTests
{
    private static readonly DocumentChunk VectorOnly = Chunks.Create("paraphrased passage about leave", index: 0);
    private static readonly DocumentChunk Both = Chunks.Create("annual leave allowance is 25 days", index: 1);
    private static readonly DocumentChunk KeywordOnly = Chunks.Create("form HR-17 requests allowance changes", index: 2);

    [Fact]
    public async Task Fuses_vector_and_keyword_rankings()
    {
        var keywordIndex = new Bm25Index();
        keywordIndex.Add([Both, KeywordOnly]);
        var retriever = CreateRetriever(new FixedVectorStore([VectorOnly, Both]), keywordIndex);

        var results = await retriever.RetrieveAsync("allowance", topK: 5, AnyTenantDefault, CancellationToken.None);

        Assert.Equal(new[] { Both.Id, VectorOnly.Id, KeywordOnly.Id }, results.Select(r => r.Chunk.Id));
        Assert.Equal((2, (int?)1), (results[0].VectorRank!.Value, results[0].KeywordRank));
        Assert.Null(results[1].KeywordRank);
        Assert.Null(results[2].VectorRank);
    }

    [Fact]
    public async Task Returns_at_most_top_k_results()
    {
        var keywordIndex = new Bm25Index();
        keywordIndex.Add([Both, KeywordOnly]);
        var retriever = CreateRetriever(new FixedVectorStore([VectorOnly, Both]), keywordIndex);

        var results = await retriever.RetrieveAsync("allowance", topK: 1, AnyTenantDefault, CancellationToken.None);

        Assert.Equal(Both.Id, Assert.Single(results).Chunk.Id);
    }

    [Fact]
    public async Task Asks_each_retriever_for_the_configured_number_of_candidates()
    {
        var store = new FixedVectorStore([VectorOnly]);
        var retriever = CreateRetriever(store, new Bm25Index(), candidates: 12);

        await retriever.RetrieveAsync("anything", topK: 3, AnyTenantDefault, CancellationToken.None);

        Assert.Equal(12, store.LastLimit);
    }

    [Fact]
    public async Task An_empty_index_returns_nothing()
    {
        var retriever = CreateRetriever(new FixedVectorStore([]), new Bm25Index());

        Assert.Empty(await retriever.RetrieveAsync("anything", 5, AnyTenantDefault, CancellationToken.None));
    }

    [Fact]
    public async Task Passes_the_filter_to_both_retrievers()
    {
        var otherTenant = Chunks.Create("allowance for another tenant", index: 3) with { TenantId = "other" };
        var keywordIndex = new Bm25Index();
        keywordIndex.Add([Both, otherTenant]);
        var store = new FixedVectorStore([VectorOnly, Both]);
        var retriever = CreateRetriever(store, keywordIndex);
        var filter = new SearchFilter(TenantId.Default) { Tags = ["hr"] };

        await retriever.RetrieveAsync("allowance", topK: 5, filter, CancellationToken.None);

        Assert.Same(filter, store.LastFilter);
        Assert.DoesNotContain(keywordIndex.Search("allowance", 10, filter), hit => hit.Chunk.Id == otherTenant.Id);
    }

    private static readonly SearchFilter AnyTenantDefault = new(TenantId.Default);

    private static HybridRetriever CreateRetriever(IVectorStore store, Bm25Index keywordIndex, int candidates = 30) =>
        new(
            new ConstantEmbeddingClient(),
            store,
            keywordIndex,
            Options.Create(new RetrievalOptions { CandidatesPerRetriever = candidates }),
            NullLogger<HybridRetriever>.Instance);

    private sealed class ConstantEmbeddingClient : IEmbeddingClient
    {
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new[] { 1f, 0f }).ToList());
    }

    /// <summary>Returns its chunks in a fixed order, as if they were the nearest neighbours.</summary>
    private sealed class FixedVectorStore(IReadOnlyList<DocumentChunk> ranked) : IVectorStore
    {
        public int LastLimit { get; private set; }

        public SearchFilter? LastFilter { get; private set; }

        public Task<IReadOnlyList<ScoredChunk>> SearchAsync(float[] vector, int limit, SearchFilter filter, CancellationToken cancellationToken)
        {
            LastLimit = limit;
            LastFilter = filter;
            IReadOnlyList<ScoredChunk> hits = ranked.Take(limit).Select((chunk, i) => new ScoredChunk(chunk, 1.0 - (i * 0.1))).ToList();
            return Task.FromResult(hits);
        }

        public Task UpsertAsync(IReadOnlyList<EmbeddedChunk> chunks, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public IAsyncEnumerable<DocumentChunk> ReadAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public IAsyncEnumerable<DocumentChunk> ReadDocumentAsync(Guid documentId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
