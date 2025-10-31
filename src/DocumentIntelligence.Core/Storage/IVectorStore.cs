using DocumentIntelligence.Core.Documents;
using DocumentIntelligence.Core.Search;

namespace DocumentIntelligence.Core.Storage;

/// <summary>Persists chunk vectors and answers nearest-neighbour queries over them.</summary>
public interface IVectorStore
{
    /// <summary>Inserts chunks, replacing any existing chunk with the same id.</summary>
    Task UpsertAsync(IReadOnlyList<EmbeddedChunk> chunks, CancellationToken cancellationToken);

    /// <summary>Returns up to <paramref name="limit"/> chunks ordered by descending cosine similarity.</summary>
    Task<IReadOnlyList<ScoredChunk>> SearchAsync(float[] vector, int limit, CancellationToken cancellationToken);

    Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken);

    /// <summary>Streams every stored chunk without its vector; used to rebuild the keyword index.</summary>
    IAsyncEnumerable<DocumentChunk> ReadAllAsync(CancellationToken cancellationToken);
}
