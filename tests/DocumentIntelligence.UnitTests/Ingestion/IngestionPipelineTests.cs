using DocumentIntelligence.Core.Chunking;
using DocumentIntelligence.Core.Documents;
using DocumentIntelligence.Core.Embeddings;
using DocumentIntelligence.Core.Ingestion;
using DocumentIntelligence.Core.Search;
using DocumentIntelligence.Infrastructure.Offline;
using DocumentIntelligence.Infrastructure.VectorStores;
using DocumentIntelligence.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.UnitTests.Ingestion;

public sealed class IngestionPipelineTests
{
    private const int Dimensions = 64;

    private readonly InMemoryVectorStore _store = new(Dimensions);
    private readonly Bm25Index _keywordIndex = new();

    [Fact]
    public async Task Writes_every_chunk_to_both_indexes()
    {
        var pipeline = CreatePipeline();
        var document = Markdown("# Guide\n\nStep one is short. Step two is short.\n\n## Next\n\nStep three is also short.");

        var count = await pipeline.IngestAsync(document, CancellationToken.None);

        var stored = await ReadStoredAsync();
        Assert.Equal(2, count);
        Assert.Equal(2, stored.Count);
        Assert.Equal(2, _keywordIndex.Count);
        Assert.All(stored, chunk => Assert.Equal(document.Id, chunk.DocumentId));
        Assert.Equal(new[] { "Guide", "Guide › Next" }, stored.OrderBy(c => c.ChunkIndex).Select(c => c.Section));
        Assert.All(stored, chunk => Assert.Equal(DocumentChunk.CreateId(document.Id, chunk.ChunkIndex), chunk.Id));
    }

    [Fact]
    public async Task Embeds_in_batches_of_the_configured_size()
    {
        var embeddings = new CountingEmbeddingClient(new HashingEmbeddingClient(Dimensions));
        var pipeline = CreatePipeline(embeddings, batchSize: 2, maxTokens: 16);
        var text = string.Join(" ", Enumerable.Range(1, 10).Select(i => $"Sentence {i} has exactly six words."));

        var count = await pipeline.IngestAsync(new SourceDocument(Guid.NewGuid(), "doc.txt", DocumentFormat.PlainText, text), CancellationToken.None);

        Assert.Equal((count + 1) / 2, embeddings.Calls);
        Assert.All(embeddings.BatchSizes, size => Assert.InRange(size, 1, 2));
    }

    [Fact]
    public async Task Re_ingesting_a_document_replaces_its_previous_chunks()
    {
        var pipeline = CreatePipeline(maxTokens: 16);
        var id = Guid.NewGuid();
        var longVersion = string.Join(" ", Enumerable.Range(1, 12).Select(i => $"Original sentence {i} is here."));

        await pipeline.IngestAsync(new SourceDocument(id, "doc.txt", DocumentFormat.PlainText, longVersion), CancellationToken.None);
        await pipeline.IngestAsync(new SourceDocument(id, "doc.txt", DocumentFormat.PlainText, "Replacement text only."), CancellationToken.None);

        var stored = Assert.Single(await ReadStoredAsync());
        Assert.Equal("Replacement text only.", stored.Text);
        Assert.Equal(1, _keywordIndex.Count);
        Assert.Empty(_keywordIndex.Search("original", 10));
    }

    [Fact]
    public async Task A_failed_batch_leaves_nothing_behind()
    {
        var failing = new FailingEmbeddingClient(new HashingEmbeddingClient(Dimensions), failOnCall: 2);
        var pipeline = CreatePipeline(failing, batchSize: 1, maxTokens: 16);
        var text = string.Join(" ", Enumerable.Range(1, 8).Select(i => $"Sentence {i} has exactly six words."));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            pipeline.IngestAsync(new SourceDocument(Guid.NewGuid(), "doc.txt", DocumentFormat.PlainText, text), CancellationToken.None));

        Assert.Empty(await ReadStoredAsync());
        Assert.Equal(0, _keywordIndex.Count);
    }

    [Fact]
    public async Task Removing_a_document_clears_both_indexes()
    {
        var pipeline = CreatePipeline();
        var kept = Markdown("Kept document text.");
        var removed = Markdown("Removed document text.");
        await pipeline.IngestAsync(kept, CancellationToken.None);
        await pipeline.IngestAsync(removed, CancellationToken.None);

        await pipeline.RemoveAsync(removed.Id, CancellationToken.None);

        Assert.Equal(kept.Id, Assert.Single(await ReadStoredAsync()).DocumentId);
        Assert.Equal(kept.Id, Assert.Single(_keywordIndex.ListDocuments()).Id);
    }

    private IngestionPipeline CreatePipeline(IEmbeddingClient? embeddings = null, int batchSize = 32, int maxTokens = 256) =>
        new(
            new TextChunker(new WordTokenCounter(), Options.Create(new ChunkingOptions { MaxTokens = maxTokens, OverlapTokens = 0 })),
            embeddings ?? new HashingEmbeddingClient(Dimensions),
            _store,
            _keywordIndex,
            Options.Create(new IngestionOptions { EmbeddingBatchSize = batchSize }),
            NullLogger<IngestionPipeline>.Instance);

    private static SourceDocument Markdown(string content) => new(Guid.NewGuid(), "doc.md", DocumentFormat.Markdown, content);

    private async Task<List<DocumentChunk>> ReadStoredAsync()
    {
        var chunks = new List<DocumentChunk>();
        await foreach (var chunk in _store.ReadAllAsync(CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    private sealed class CountingEmbeddingClient(IEmbeddingClient inner) : IEmbeddingClient
    {
        public int Calls => BatchSizes.Count;

        public List<int> BatchSizes { get; } = [];

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
        {
            BatchSizes.Add(texts.Count);
            return inner.EmbedAsync(texts, cancellationToken);
        }
    }
}
