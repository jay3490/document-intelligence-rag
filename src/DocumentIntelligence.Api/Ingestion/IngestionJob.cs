namespace DocumentIntelligence.Api.Ingestion;

public enum IngestionStatus
{
    Queued,
    Processing,
    Completed,
    Failed,
}

/// <summary>The progress of one uploaded document through the background ingestion worker.</summary>
public sealed record IngestionJob(
    Guid Id,
    Guid DocumentId,
    string FileName,
    long SizeBytes,
    IngestionStatus Status,
    int? ChunkCount,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt)
{
    public string TenantId { get; init; } = Core.Tenancy.TenantId.Default;
}
