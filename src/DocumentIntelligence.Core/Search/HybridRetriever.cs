using DocumentIntelligence.Core.Documents;
using DocumentIntelligence.Core.Embeddings;
using DocumentIntelligence.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Core.Search;

/// <summary>
/// Retrieves chunks by running vector similarity and BM25 keyword search side by side and
/// fusing the two rankings with Reciprocal Rank Fusion.
/// </summary>
/// <remarks>
/// Vector search finds passages that paraphrase the question; BM25 finds passages that share
/// its exact terms (identifiers, error codes, names) which embeddings tend to blur. Fusing on
/// rank rather than score avoids having to calibrate cosine similarity against BM25.
/// </remarks>
public sealed partial class HybridRetriever(
    IEmbeddingClient embeddingClient,
    IVectorStore vectorStore,
    Bm25Index keywordIndex,
    IOptions<RetrievalOptions> options,
    ILogger<HybridRetriever> logger)
{
    private readonly RetrievalOptions _options = options.Value;

    public async Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, int topK, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        using var activity = Telemetry.ActivitySource.StartActivity("retrieve");
        var candidates = Math.Max(topK, _options.CandidatesPerRetriever);

        var embeddings = await embeddingClient.EmbedAsync([query], cancellationToken);
        var vectorHits = await vectorStore.SearchAsync(embeddings[0], candidates, cancellationToken);
        var keywordHits = keywordIndex.Search(query, candidates);

        var chunksById = new Dictionary<Guid, DocumentChunk>();
        foreach (var hit in vectorHits.Concat(keywordHits))
        {
            chunksById.TryAdd(hit.Chunk.Id, hit.Chunk);
        }

        IReadOnlyList<Guid>[] rankings =
        [
            vectorHits.Select(hit => hit.Chunk.Id).ToList(),
            keywordHits.Select(hit => hit.Chunk.Id).ToList(),
        ];

        var results = ReciprocalRankFusion.Fuse(rankings, _options.RrfK)
            .Take(topK)
            .Select(fused => new RetrievedChunk(chunksById[fused.Key], fused.Score, fused.Ranks[0], fused.Ranks[1]))
            .ToList();

        activity?.SetTag("retrieval.vector_hits", vectorHits.Count);
        activity?.SetTag("retrieval.keyword_hits", keywordHits.Count);
        activity?.SetTag("retrieval.results", results.Count);
        LogRetrieved(logger, vectorHits.Count, keywordHits.Count, results.Count);

        return results;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hybrid retrieval: {VectorHits} vector hits, {KeywordHits} keyword hits, {Results} fused results")]
    private static partial void LogRetrieved(ILogger logger, int vectorHits, int keywordHits, int results);
}
