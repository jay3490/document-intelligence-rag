using DocumentIntelligence.Core;
using DocumentIntelligence.Core.Answering;
using DocumentIntelligence.Core.Ingestion;

namespace DocumentIntelligence.Api.Ingestion;

/// <summary>
/// Drains the ingestion queue one document at a time and records each job's outcome. Work
/// starts only after the keyword index has been rebuilt, so a fresh upload is never
/// overwritten or duplicated by the start-up load.
/// </summary>
public sealed partial class IngestionWorker(
    IngestionQueue queue,
    IIngestionJobStore jobs,
    IngestionPipeline pipeline,
    IQueryResultCache queryCache,
    KeywordIndexInitializer keywordIndexInitializer,
    ILogger<IngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await keywordIndexInitializer.WaitUntilReadyAsync(stoppingToken);

        await foreach (var item in queue.ReadAllAsync(stoppingToken))
        {
            if (!await jobs.TryStartAttemptAsync(item.JobId, stoppingToken))
            {
                continue;
            }

            try
            {
                var chunkCount = await pipeline.IngestAsync(item.Document, stoppingToken);
                // Invalidate first, so a client that sees "Completed" never gets a stale answer.
                await queryCache.InvalidateTenantAsync(item.Document.TenantId, stoppingToken);
                await jobs.MarkCompletedAsync(item.JobId, chunkCount, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                await jobs.MarkFailedAsync(item.JobId, "The service stopped before ingestion finished.", CancellationToken.None);
                throw;
            }
            catch (UpstreamServiceException exception)
            {
                LogFailed(logger, exception, item.Document.Name, item.JobId);
                await jobs.MarkFailedAsync(item.JobId, exception.Message, stoppingToken);
            }
            catch (Exception exception)
            {
                LogFailed(logger, exception, item.Document.Name, item.JobId);
                await jobs.MarkFailedAsync(item.JobId, "Ingestion failed unexpectedly. See the service logs for details.", stoppingToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Ingestion of {DocumentName} failed (job {JobId})")]
    private static partial void LogFailed(ILogger logger, Exception exception, string documentName, Guid jobId);
}
