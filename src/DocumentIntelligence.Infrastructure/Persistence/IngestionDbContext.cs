using DocumentIntelligence.Core.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DocumentIntelligence.Infrastructure.Persistence;

/// <summary>Ingestion job state, stored in PostgreSQL in deployments and SQLite locally.</summary>
public sealed class IngestionDbContext(DbContextOptions<IngestionDbContext> options) : DbContext(options)
{
    public DbSet<IngestionJobEntity> IngestionJobs => Set<IngestionJobEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var job = modelBuilder.Entity<IngestionJobEntity>();
        job.ToTable("ingestion_jobs");
        job.HasKey(j => j.Id);
        job.Property(j => j.TenantId).HasMaxLength(64).IsRequired();
        job.Property(j => j.FileName).HasMaxLength(260).IsRequired();
        job.Property(j => j.ContentHash).HasMaxLength(64).IsFixedLength().IsRequired();
        job.Property(j => j.BlobName).HasMaxLength(1024).IsRequired();
        job.Property(j => j.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        job.Property(j => j.Error).HasMaxLength(2000);
        job.Property(j => j.CorrelationId).HasMaxLength(128);

        job.HasIndex(j => new { j.TenantId, j.CreatedAt });
        job.HasIndex(j => new { j.TenantId, j.ContentHash });
        job.HasIndex(j => j.DocumentId);

        // SQLite cannot order or compare DateTimeOffset values natively; store them as integers.
        if (Database.IsSqlite())
        {
            foreach (var property in modelBuilder.Model.GetEntityTypes()
                         .SelectMany(entity => entity.GetProperties())
                         .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
            {
                property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
            }
        }
    }
}

public sealed class IngestionJobEntity
{
    public Guid Id { get; set; }

    public string TenantId { get; set; } = string.Empty;

    public Guid DocumentId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string ContentHash { get; set; } = string.Empty;

    public string BlobName { get; set; } = string.Empty;

    public IngestionStatus Status { get; set; }

    public int? ChunkCount { get; set; }

    public int Attempts { get; set; }

    public string? Error { get; set; }

    public Guid? DuplicateOfDocumentId { get; set; }

    public string? CorrelationId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>When the job's document was deleted; such content no longer counts as indexed.</summary>
    public DateTimeOffset? DocumentRemovedAt { get; set; }

    public static IngestionJobEntity From(IngestionJob job) => new()
    {
        Id = job.Id,
        TenantId = job.TenantId,
        DocumentId = job.DocumentId,
        FileName = job.FileName,
        SizeBytes = job.SizeBytes,
        ContentHash = job.ContentHash,
        BlobName = job.BlobName,
        Status = job.Status,
        ChunkCount = job.ChunkCount,
        Attempts = job.Attempts,
        Error = job.Error,
        DuplicateOfDocumentId = job.DuplicateOfDocumentId,
        CorrelationId = job.CorrelationId,
        CreatedAt = job.CreatedAt,
        StartedAt = job.StartedAt,
        CompletedAt = job.CompletedAt,
    };

    public IngestionJob ToJob() => new()
    {
        Id = Id,
        TenantId = TenantId,
        DocumentId = DocumentId,
        FileName = FileName,
        SizeBytes = SizeBytes,
        ContentHash = ContentHash,
        BlobName = BlobName,
        Status = Status,
        ChunkCount = ChunkCount,
        Attempts = Attempts,
        Error = Error,
        DuplicateOfDocumentId = DuplicateOfDocumentId,
        CorrelationId = CorrelationId,
        CreatedAt = CreatedAt,
        StartedAt = StartedAt,
        CompletedAt = CompletedAt,
    };
}
