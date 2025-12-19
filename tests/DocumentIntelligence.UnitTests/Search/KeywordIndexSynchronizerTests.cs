using DocumentIntelligence.Core.Messaging;
using DocumentIntelligence.Core.Search;
using DocumentIntelligence.Core.Storage;
using DocumentIntelligence.Infrastructure.VectorStores;
using DocumentIntelligence.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocumentIntelligence.UnitTests.Search;

public sealed class KeywordIndexSynchronizerTests
{
    private readonly InMemoryVectorStore _store = new(2);
    private readonly Bm25Index _index = new();

    [Fact]
    public async Task An_indexed_document_is_loaded_from_the_vector_store()
    {
        var documentId = Guid.NewGuid();
        await StoreAsync(Chunks.Create("alpha text", documentId, 0), Chunks.Create("beta text", documentId, 1));

        await Synchronizer().ApplyAsync(Change(documentId, IndexChange.Indexed), CancellationToken.None);

        Assert.Equal(2, _index.Count);
        Assert.Single(_index.Search("beta", 5));
    }

    [Fact]
    public async Task Re_applying_an_indexed_change_replaces_stale_chunks()
    {
        var documentId = Guid.NewGuid();
        _index.Add([Chunks.Create("stale words", documentId, 0), Chunks.Create("more stale words", documentId, 1)]);
        await StoreAsync(Chunks.Create("fresh words", documentId, 0));

        await Synchronizer().ApplyAsync(Change(documentId, IndexChange.Indexed), CancellationToken.None);
        await Synchronizer().ApplyAsync(Change(documentId, IndexChange.Indexed), CancellationToken.None);

        Assert.Equal(1, _index.Count);
        Assert.Empty(_index.Search("stale", 5));
    }

    [Fact]
    public async Task A_removed_document_leaves_the_keyword_index()
    {
        var removed = Guid.NewGuid();
        _index.Add([Chunks.Create("gone", removed), Chunks.Create("kept")]);

        await Synchronizer().ApplyAsync(Change(removed, IndexChange.Removed), CancellationToken.None);

        Assert.Equal(1, _index.Count);
        Assert.Empty(_index.Search("gone", 5));
    }

    private KeywordIndexSynchronizer Synchronizer() => new(_store, _index, NullLogger<KeywordIndexSynchronizer>.Instance);

    private static DocumentIndexChanged Change(Guid documentId, IndexChange change) =>
        new("default", documentId, change, DateTimeOffset.UtcNow, null);

    private Task StoreAsync(params Core.Documents.DocumentChunk[] chunks) =>
        _store.UpsertAsync(chunks.Select(c => new EmbeddedChunk(c, [1f, 0f])).ToList(), CancellationToken.None);
}
