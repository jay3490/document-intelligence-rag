using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using DocumentIntelligence.Core.Documents;
using DocumentIntelligence.Core.Search;
using DocumentIntelligence.Core.Storage;

namespace DocumentIntelligence.Infrastructure.VectorStores;

/// <summary>
/// A process-local vector store that scans every vector on each search. Contents are lost on
/// restart; it exists for tests and for running without Qdrant.
/// </summary>
public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly ConcurrentDictionary<Guid, EmbeddedChunk> _chunks = new();
    private readonly int _dimensions;

    public InMemoryVectorStore(int dimensions)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);
        _dimensions = dimensions;
    }

    public Task UpsertAsync(IReadOnlyList<EmbeddedChunk> chunks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        foreach (var chunk in chunks)
        {
            EnsureDimensions(chunk.Vector);
            _chunks[chunk.Chunk.Id] = chunk;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ScoredChunk>> SearchAsync(float[] vector, int limit, CancellationToken cancellationToken)
    {
        EnsureDimensions(vector);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        IReadOnlyList<ScoredChunk> hits = _chunks.Values
            .Select(stored => new ScoredChunk(stored.Chunk, CosineSimilarity(vector, stored.Vector)))
            .OrderByDescending(hit => hit.Score)
            .ThenBy(hit => hit.Chunk.DocumentName, StringComparer.Ordinal)
            .ThenBy(hit => hit.Chunk.ChunkIndex)
            .Take(limit)
            .ToList();

        return Task.FromResult(hits);
    }

    public Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        foreach (var (id, stored) in _chunks)
        {
            if (stored.Chunk.DocumentId == documentId)
            {
                _chunks.TryRemove(id, out _);
            }
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<DocumentChunk> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var stored in _chunks.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return stored.Chunk;
        }

        await Task.CompletedTask;
    }

    private void EnsureDimensions(float[] vector)
    {
        ArgumentNullException.ThrowIfNull(vector);
        if (vector.Length != _dimensions)
        {
            throw new ArgumentException(
                $"Expected a vector with {_dimensions} dimensions but got {vector.Length}.", nameof(vector));
        }
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return normA == 0 || normB == 0 ? 0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
