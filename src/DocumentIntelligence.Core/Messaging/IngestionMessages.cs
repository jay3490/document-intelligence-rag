using DocumentIntelligence.Core.Documents;
using DocumentIntelligence.Core.Ingestion;

namespace DocumentIntelligence.Core.Messaging;

/// <summary>
/// Published when a document's raw content has been stored and is ready to be ingested. The
/// message carries everything needed to create the job record, so producers that never touch
/// the job database (the blob-trigger function) can publish it too.
/// </summary>
public sealed record DocumentUploaded
{
    public const int SchemaVersion = 1;

    public required Guid JobId { get; init; }

    public required string TenantId { get; init; }

    public required Guid DocumentId { get; init; }

    public required string FileName { get; init; }

    public required string BlobName { get; init; }

    /// <summary>Lower-case hex SHA-256 of the UTF-8 content; duplicates are detected by it.</summary>
    public required string ContentHash { get; init; }

    public required long SizeBytes { get; init; }

    public string? Source { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    public DateTimeOffset? Date { get; init; }

    public required DateTimeOffset UploadedAt { get; init; }

    public string? CorrelationId { get; init; }

    public DocumentMetadata ToMetadata() => new() { Source = Source, Tags = Tags, Date = Date };

    public IngestionJob ToNewJob() => new()
    {
        Id = JobId,
        TenantId = TenantId,
        DocumentId = DocumentId,
        FileName = FileName,
        SizeBytes = SizeBytes,
        ContentHash = ContentHash,
        BlobName = BlobName,
        CorrelationId = CorrelationId,
        CreatedAt = UploadedAt,
    };
}

/// <summary>An upload that could not be ingested, kept on the dead-letter topic for inspection or replay.</summary>
public sealed record DeadLetteredUpload(DocumentUploaded Message, string Error, int Attempts, DateTimeOffset FailedAt);

public enum IndexChange
{
    Indexed,
    Removed,
}

/// <summary>
/// Tells query services that a document's chunks changed in the vector store, so they can bring
/// their in-memory keyword index up to date.
/// </summary>
public sealed record DocumentIndexChanged(
    string TenantId,
    Guid DocumentId,
    IndexChange Change,
    DateTimeOffset ChangedAt,
    string? CorrelationId);

/// <summary>Sends ingestion messages over the configured transport.</summary>
public interface IIngestionEventPublisher
{
    /// <exception cref="IngestionBackpressureException">The transport cannot accept more uploads right now.</exception>
    Task PublishUploadedAsync(DocumentUploaded message, CancellationToken cancellationToken);

    Task PublishDeadLetterAsync(DeadLetteredUpload message, CancellationToken cancellationToken);

    Task PublishIndexChangedAsync(DocumentIndexChanged message, CancellationToken cancellationToken);
}

/// <summary>Raised when an upload cannot be queued because the transport is full.</summary>
public sealed class IngestionBackpressureException(string message) : Exception(message);
