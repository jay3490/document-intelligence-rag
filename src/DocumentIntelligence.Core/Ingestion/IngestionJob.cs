namespace DocumentIntelligence.Core.Ingestion;

public enum IngestionStatus
{
    Queued,
    Processing,
    Completed,
    Failed,

    /// <summary>The tenant already has this exact content indexed; nothing was re-ingested.</summary>
    Duplicate,
}

/// <summary>The progress of one uploaded document through ingestion.</summary>
public sealed record IngestionJob
{
    public required Guid Id { get; init; }

    public required string TenantId { get; init; }

    public required Guid DocumentId { get; init; }

    public required string FileName { get; init; }

    public required long SizeBytes { get; init; }

    /// <summary>Lower-case hex SHA-256 of the document's UTF-8 text.</summary>
    public required string ContentHash { get; init; }

    /// <summary>Where the raw upload is kept in the blob store.</summary>
    public required string BlobName { get; init; }

    public IngestionStatus Status { get; init; } = IngestionStatus.Queued;

    public int? ChunkCount { get; init; }

    /// <summary>How many times processing has been attempted.</summary>
    public int Attempts { get; init; }

    public string? Error { get; init; }

    /// <summary>For a <see cref="IngestionStatus.Duplicate"/> job, the document that already holds this content.</summary>
    public Guid? DuplicateOfDocumentId { get; init; }

    /// <summary>Correlates the upload request with the messages and logs it caused.</summary>
    public string? CorrelationId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public bool IsFinished => Status is IngestionStatus.Completed or IngestionStatus.Failed or IngestionStatus.Duplicate;
}
