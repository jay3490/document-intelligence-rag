using DocumentIntelligence.Core.Documents;
using DocumentIntelligence.Core.Messaging;
using DocumentIntelligence.Core.Storage;
using Microsoft.Extensions.Logging;

namespace DocumentIntelligence.Core.Search;

/// <summary>
/// Applies an index change made by another process to this process's keyword index. An indexed
/// document is re-read from the vector store, which stays the source of truth, so applying the
/// same change twice is harmless.
/// </summary>
public sealed partial class KeywordIndexSynchronizer(IVectorStore vectorStore, Bm25Index keywordIndex, ILogger<KeywordIndexSynchronizer> logger)
{
    public async Task ApplyAsync(DocumentIndexChanged change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (change.Change == IndexChange.Removed)
        {
            keywordIndex.RemoveDocument(change.DocumentId);
            LogApplied(logger, change.DocumentId, change.Change, 0);
            return;
        }

        var chunks = new List<DocumentChunk>();
        await foreach (var chunk in vectorStore.ReadDocumentAsync(change.DocumentId, cancellationToken))
        {
            chunks.Add(chunk);
        }

        // Replace rather than merge, so chunks that disappeared on re-ingestion are dropped too.
        keywordIndex.RemoveDocument(change.DocumentId);
        keywordIndex.Add(chunks);
        LogApplied(logger, change.DocumentId, change.Change, chunks.Count);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Keyword index updated for document {DocumentId} ({Change}, {ChunkCount} chunks)")]
    private static partial void LogApplied(ILogger logger, Guid documentId, IndexChange change, int chunkCount);
}
