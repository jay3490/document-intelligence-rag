namespace DocumentIntelligence.Core.Search;

/// <summary>
/// Reorders retrieved candidates by how well each one answers the query, looking at the query
/// and passage together rather than comparing independent embeddings.
/// </summary>
public interface IReranker
{
    /// <returns>At most <paramref name="topK"/> of the candidates, best first.</returns>
    Task<IReadOnlyList<RetrievedChunk>> RerankAsync(
        string query,
        IReadOnlyList<RetrievedChunk> candidates,
        int topK,
        CancellationToken cancellationToken);
}

/// <summary>Keeps the fused retrieval order; the default when no reranker is configured.</summary>
public sealed class NoOpReranker : IReranker
{
    public Task<IReadOnlyList<RetrievedChunk>> RerankAsync(
        string query,
        IReadOnlyList<RetrievedChunk> candidates,
        int topK,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);
        return Task.FromResult<IReadOnlyList<RetrievedChunk>>(candidates.Take(topK).ToList());
    }
}
