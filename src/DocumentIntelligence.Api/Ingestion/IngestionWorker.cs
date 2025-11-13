using DocumentIntelligence.Core;
using DocumentIntelligence.Core.Ingestion;

namespace DocumentIntelligence.Api.Ingestion;

/// <summary>
/// Drains the ingestion queue one document at a time and records each job's outcome. Work
/// starts only after the keyword index has been rebuilt, so a fresh upload is never
/// overwritten or duplicated by the start-up load.
/// </summary>
public sealed partial class IngestionWorker(
    IngestionQueue queue,
    IngestionJobStore jobs,
    IngestionPipeline pipeline,
    KeywordIndexInitializer keywordIndexInitializer,
    ILogger<IngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await keywordIndexInitializer.WaitUntilReadyAsync(stoppingToken);

        await foreach (var item in queue.ReadAllAsync(stoppingToken))
        {
            jobs.MarkProcessing(item.JobId);
            try
            {
                var chunkCount = await pipeline.IngestAsync(item.Document, stoppingToken);
                jobs.MarkCompleted(item.JobId, chunkCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                jobs.MarkFailed(item.JobId, "The service stopped before ingestion finished.");
                throw;
            }
            catch (UpstreamServiceException exception)
            {
                LogFailed(logger, exception, item.Document.Name, item.JobId);
                jobs.MarkFailed(item.JobId, exception.Message);
            }
            catch (Exception exception)
            {
                LogFailed(logger, exception, item.Document.Name, item.JobId);
                jobs.MarkFailed(item.JobId, "Ingestion failed unexpectedly. See the service logs for details.");
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Ingestion of {DocumentName} failed (job {JobId})")]
    private static partial void LogFailed(ILogger logger, Exception exception, string documentName, Guid jobId);
}
