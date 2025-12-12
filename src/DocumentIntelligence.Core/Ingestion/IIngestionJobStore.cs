namespace DocumentIntelligence.Core.Ingestion;

/// <summary>Durable record of ingestion jobs, shared by the API and the ingestion worker.</summary>
public interface IIngestionJobStore
{
    /// <summary>Adds a job; adding a job id that already exists leaves the stored job unchanged.</summary>
    /// <returns>The stored job, which is the existing one when the id was already known.</returns>
    Task<IngestionJob> AddAsync(IngestionJob job, CancellationToken cancellationToken);

    Task<IngestionJob?> GetAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>A tenant's most recent jobs, newest first.</summary>
    Task<IReadOnlyList<IngestionJob>> ListAsync(string tenantId, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Moves a job to <see cref="IngestionStatus.Processing"/> and counts the attempt, unless it has
    /// already finished successfully or as a duplicate.
    /// </summary>
    /// <returns>False when the job is unknown or already done, so the caller must not process it.</returns>
    Task<bool> TryStartAttemptAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>Records a failed attempt that will be retried.</summary>
    Task RecordAttemptFailureAsync(Guid jobId, string error, CancellationToken cancellationToken);

    Task MarkCompletedAsync(Guid jobId, int chunkCount, CancellationToken cancellationToken);

    Task MarkFailedAsync(Guid jobId, string error, CancellationToken cancellationToken);

    Task MarkDuplicateAsync(Guid jobId, Guid existingDocumentId, CancellationToken cancellationToken);

    /// <summary>
    /// The completed job of another document of the same tenant with identical content, if that
    /// document is still indexed.
    /// </summary>
    Task<IngestionJob?> FindIndexedDuplicateAsync(string tenantId, string contentHash, Guid excludingJobId, CancellationToken cancellationToken);

    /// <summary>Notes that a document was deleted, so its content no longer counts as indexed.</summary>
    Task MarkDocumentRemovedAsync(Guid documentId, CancellationToken cancellationToken);

    /// <summary>Removes a job that never got going, for example because it could not be queued.</summary>
    Task DeleteAsync(Guid jobId, CancellationToken cancellationToken);
}
