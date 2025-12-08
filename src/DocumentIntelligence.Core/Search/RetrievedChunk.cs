using DocumentIntelligence.Core.Documents;

namespace DocumentIntelligence.Core.Search;

/// <summary>
/// A chunk selected by hybrid retrieval. <see cref="Score"/> is the fused RRF score; the ranks
/// show where the chunk placed in each retriever (null when that retriever did not return it).
/// </summary>
public sealed record RetrievedChunk(DocumentChunk Chunk, double Score, int? VectorRank, int? KeywordRank)
{
    /// <summary>The reranker's grade (0 to 10), when a reranker graded this chunk.</summary>
    public double? RerankScore { get; init; }
}
