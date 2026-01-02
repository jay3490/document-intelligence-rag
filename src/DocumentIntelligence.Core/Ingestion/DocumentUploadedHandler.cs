using System.Text;
using DocumentIntelligence.Core.Answering;
using DocumentIntelligence.Core.Documents;
using DocumentIntelligence.Core.Messaging;
using DocumentIntelligence.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Core.Ingestion;

public enum UploadOutcome
{
    Completed,

    /// <summary>The tenant already had identical content indexed; the job points at that document.</summary>
    Duplicate,

    /// <summary>The job had already finished, so a redelivered message was ignored.</summary>
    AlreadyProcessed,

    /// <summary>Every attempt failed, or the failure could not be fixed by retrying.</summary>
    DeadLettered,
}

/// <summary>
/// Ingests one uploaded document: reads it from the blob store, chunks, embeds and upserts it,
/// and records the outcome. The same handler serves the in-process channel and Kafka.
/// </summary>
/// <remarks>
/// Delivery is at least once, so handling is idempotent: the job record is created only if
/// missing, a finished job is never processed again, and content the tenant already has
/// indexed (same SHA-256) is recorded as a duplicate instead of being ingested twice.
/// Transient failures (dependencies, I/O) are retried with exponential back-off; after the last
/// attempt, or on a failure retrying cannot fix, the message goes to the dead-letter topic.
/// </remarks>
public sealed partial class DocumentUploadedHandler(
    IIngestionJobStore jobs,
    IBlobStore blobs,
    IngestionPipeline pipeline,
    IQueryResultCache queryCache,
    IIngestionEventPublisher publisher,
    IOptions<IngestionOptions> options,
    TimeProvider timeProvider,
    ILogger<DocumentUploadedHandler> logger)
{
    public async Task<UploadOutcome> HandleAsync(DocumentUploaded message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = message.CorrelationId,
            ["JobId"] = message.JobId,
            ["TenantId"] = message.TenantId,
        });
        using var activity = Telemetry.ActivitySource.StartActivity("handle document uploaded");
        activity?.SetTag("ingestion.job_id", message.JobId);
        activity?.SetTag("correlation.id", message.CorrelationId);

        await jobs.AddAsync(message.ToNewJob(), cancellationToken);
        Telemetry.IngestionLag.Record(Math.Max(0, (timeProvider.GetUtcNow() - message.UploadedAt).TotalSeconds));

        var outcome = await HandleAttemptsAsync(message, cancellationToken);
        Telemetry.IngestedDocuments.Add(1, new KeyValuePair<string, object?>("outcome", OutcomeTag(outcome)));
        activity?.SetTag("ingestion.outcome", OutcomeTag(outcome));
        return outcome;
    }

    private async Task<UploadOutcome> HandleAttemptsAsync(DocumentUploaded message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        for (var attempt = 1; ; attempt++)
        {
            if (!await jobs.TryStartAttemptAsync(message.JobId, cancellationToken))
            {
                LogAlreadyProcessed(logger, message.JobId);
                return UploadOutcome.AlreadyProcessed;
            }

            try
            {
                return await ProcessAsync(message, cancellationToken);
            }
            catch (Exception exception) when (IsTransient(exception) && attempt < settings.MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                var delay = settings.RetryDelay(attempt + 1);
                LogRetrying(logger, exception, message.JobId, attempt, settings.MaxAttempts, delay.TotalMilliseconds);
                await jobs.RecordAttemptFailureAsync(message.JobId, Describe(exception), cancellationToken);
                await Task.Delay(delay, timeProvider, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                await DeadLetterAsync(message, exception, attempt, cancellationToken);
                return UploadOutcome.DeadLettered;
            }
        }
    }

    private static string OutcomeTag(UploadOutcome outcome) => outcome switch
    {
        UploadOutcome.Completed => "completed",
        UploadOutcome.Duplicate => "duplicate",
        UploadOutcome.AlreadyProcessed => "already_processed",
        _ => "dead_lettered",
    };

    /// <summary>Failures a later attempt can plausibly get past.</summary>
    public static bool IsTransient(Exception exception) =>
        exception is UpstreamServiceException or HttpRequestException or TimeoutException or IOException;

    private async Task<UploadOutcome> ProcessAsync(DocumentUploaded message, CancellationToken cancellationToken)
    {
        if (await jobs.FindIndexedDuplicateAsync(message.TenantId, message.ContentHash, message.JobId, cancellationToken) is { } existing)
        {
            await jobs.MarkDuplicateAsync(message.JobId, existing.DocumentId, cancellationToken);
            LogDuplicate(logger, message.JobId, existing.DocumentId);
            return UploadOutcome.Duplicate;
        }

        if (!DocumentFormats.TryFromFileName(message.FileName, out var format))
        {
            throw new InvalidDataException($"'{message.FileName}' is not a supported document type.");
        }

        var content = await ReadContentAsync(message.BlobName, cancellationToken);
        var document = new SourceDocument(message.DocumentId, message.FileName, format, content)
        {
            TenantId = message.TenantId,
            Metadata = message.ToMetadata(),
        };

        var chunkCount = await pipeline.IngestAsync(document, cancellationToken);

        // Invalidate and announce before completing, so a client that sees "Completed" never
        // gets an answer computed without this document.
        await queryCache.InvalidateTenantAsync(message.TenantId, cancellationToken);
        await publisher.PublishIndexChangedAsync(
            new DocumentIndexChanged(message.TenantId, message.DocumentId, IndexChange.Indexed, timeProvider.GetUtcNow(), message.CorrelationId),
            cancellationToken);
        await jobs.MarkCompletedAsync(message.JobId, chunkCount, cancellationToken);
        return UploadOutcome.Completed;
    }

    private async Task<string> ReadContentAsync(string blobName, CancellationToken cancellationToken)
    {
        await using var stream = await blobs.OpenReadAsync(blobName, cancellationToken)
            ?? throw new InvalidDataException($"The uploaded content '{blobName}' no longer exists in the blob store.");

        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        using var reader = new StreamReader(stream, strictUtf8, detectEncodingFromByteOrderMarks: true);
        string content;
        try
        {
            content = await reader.ReadToEndAsync(cancellationToken);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("The document is not UTF-8 text.", exception);
        }

        if (string.IsNullOrWhiteSpace(content) || content.Contains('\0'))
        {
            throw new InvalidDataException("The document contains no text.");
        }

        return content;
    }

    private async Task DeadLetterAsync(DocumentUploaded message, Exception exception, int attempts, CancellationToken cancellationToken)
    {
        var error = Describe(exception);
        LogDeadLettered(logger, exception, message.JobId, attempts);

        // Record the failure even if the host is shutting down.
        await jobs.MarkFailedAsync(message.JobId, error, CancellationToken.None);
        await publisher.PublishDeadLetterAsync(new DeadLetteredUpload(message, error, attempts, timeProvider.GetUtcNow()), cancellationToken);
    }

    private static string Describe(Exception exception) => exception switch
    {
        UpstreamServiceException or InvalidDataException => exception.Message,
        HttpRequestException or TimeoutException or IOException => $"A dependency failed: {exception.Message}",
        OperationCanceledException => "Ingestion was cancelled before it finished.",
        _ => "Ingestion failed unexpectedly. See the service logs for details.",
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {JobId} has already finished; ignoring the redelivered message")]
    private static partial void LogAlreadyProcessed(ILogger logger, Guid jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {JobId} duplicates the content of document {DocumentId}; not re-ingesting")]
    private static partial void LogDuplicate(ILogger logger, Guid jobId, Guid documentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {JobId} attempt {Attempt} of {MaxAttempts} failed; retrying in {DelayMilliseconds} ms")]
    private static partial void LogRetrying(ILogger logger, Exception exception, Guid jobId, int attempt, int maxAttempts, double delayMilliseconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {JobId} failed after {Attempts} attempt(s) and was dead-lettered")]
    private static partial void LogDeadLettered(ILogger logger, Exception exception, Guid jobId, int attempts);
}
