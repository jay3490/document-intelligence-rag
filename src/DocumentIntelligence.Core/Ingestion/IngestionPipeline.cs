using DocumentIntelligence.Core.Chunking;
using DocumentIntelligence.Core.Documents;
using DocumentIntelligence.Core.Embeddings;
using DocumentIntelligence.Core.Search;
using DocumentIntelligence.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Core.Ingestion;

/// <summary>
/// Chunks a document, embeds the chunks in batches and writes them to the vector store and the
/// keyword index. Ingesting a document id that already exists replaces its previous chunks.
/// </summary>
public sealed partial class IngestionPipeline(
    TextChunker chunker,
    IEmbeddingClient embeddingClient,
    IVectorStore vectorStore,
    IKeywordIndexWriter keywordIndex,
    IOptions<IngestionOptions> options,
    ILogger<IngestionPipeline> logger)
{
    /// <returns>The number of chunks written.</returns>
    public async Task<int> IngestAsync(SourceDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        using var activity = Telemetry.ActivitySource.StartActivity("ingest document");
        activity?.SetTag("document.id", document.Id);
        activity?.SetTag("document.tenant", document.TenantId);
        activity?.SetTag("document.format", document.Format.ToString());

        var chunks = chunker.Chunk(document.Content, document.Format)
            .Select(c => new DocumentChunk(
                DocumentChunk.CreateId(document.Id, c.Index),
                document.Id,
                document.Name,
                c.Index,
                c.Section,
                c.Text,
                c.TokenCount)
            {
                TenantId = document.TenantId,
                Metadata = document.Metadata,
            })
            .ToList();

        await RemoveAsync(document.Id, cancellationToken);

        try
        {
            foreach (var batch in chunks.Chunk(options.Value.EmbeddingBatchSize))
            {
                var vectors = await embeddingClient.EmbedAsync(batch.Select(c => c.SearchText).ToList(), cancellationToken);
                if (vectors.Count != batch.Length)
                {
                    throw new UpstreamServiceException(
                        "embeddings",
                        $"The embedding service returned {vectors.Count} vectors for {batch.Length} inputs.");
                }

                await vectorStore.UpsertAsync(batch.Zip(vectors, (chunk, vector) => new EmbeddedChunk(chunk, vector)).ToList(), cancellationToken);
            }
        }
        catch
        {
            // Do not leave a half-written document behind; a partial index answers badly.
            await TryRemovePartialDocumentAsync(document.Id);
            throw;
        }

        keywordIndex.Add(chunks);

        activity?.SetTag("document.chunks", chunks.Count);
        LogIngested(logger, document.Name, document.Id, chunks.Count);
        return chunks.Count;
    }

    /// <summary>Removes every chunk of a document from both indexes.</summary>
    public async Task RemoveAsync(Guid documentId, CancellationToken cancellationToken)
    {
        await vectorStore.DeleteDocumentAsync(documentId, cancellationToken);
        keywordIndex.RemoveDocument(documentId);
    }

    private async Task TryRemovePartialDocumentAsync(Guid documentId)
    {
        try
        {
            await vectorStore.DeleteDocumentAsync(documentId, CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogCleanupFailed(logger, exception, documentId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ingested {DocumentName} ({DocumentId}) as {ChunkCount} chunks")]
    private static partial void LogIngested(ILogger logger, string documentName, Guid documentId, int chunkCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not remove the partially ingested document {DocumentId}")]
    private static partial void LogCleanupFailed(ILogger logger, Exception exception, Guid documentId);
}
