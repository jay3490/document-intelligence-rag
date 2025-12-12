using DocumentIntelligence.Core.Ingestion;
using Microsoft.EntityFrameworkCore;

namespace DocumentIntelligence.Infrastructure.Persistence;

/// <summary>
/// <see cref="IIngestionJobStore"/> on EF Core. State changes are single UPDATE statements with
/// the allowed source states in the WHERE clause, so concurrent API replicas and workers cannot
/// move a finished job backwards.
/// </summary>
public sealed class EfIngestionJobStore(IDbContextFactory<IngestionDbContext> contextFactory, TimeProvider timeProvider) : IIngestionJobStore
{
    public const int MaxErrorLength = 2000;

    public async Task<IngestionJob> AddAsync(IngestionJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (await context.IngestionJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == job.Id, cancellationToken) is { } existing)
        {
            return existing.ToJob();
        }

        context.IngestionJobs.Add(IngestionJobEntity.From(job));
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return job;
        }
        catch (DbUpdateException)
        {
            // Lost a race with another writer adding the same job id.
            context.ChangeTracker.Clear();
            var stored = await context.IngestionJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == job.Id, cancellationToken);
            return stored?.ToJob() ?? throw new InvalidOperationException($"Ingestion job {job.Id} could not be stored.");
        }
    }

    public async Task<IngestionJob?> GetAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.IngestionJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        return entity?.ToJob();
    }

    public async Task<IReadOnlyList<IngestionJob>> ListAsync(string tenantId, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.IngestionJobs.AsNoTracking()
            .Where(j => j.TenantId == tenantId)
            .OrderByDescending(j => j.CreatedAt)
            .ThenBy(j => j.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return entities.Select(e => e.ToJob()).ToList();
    }

    public async Task<bool> TryStartAttemptAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var updated = await context.IngestionJobs
            .Where(j => j.Id == jobId && j.Status != IngestionStatus.Completed && j.Status != IngestionStatus.Duplicate)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(j => j.Status, IngestionStatus.Processing)
                    .SetProperty(j => j.Attempts, j => j.Attempts + 1)
                    .SetProperty(j => j.StartedAt, j => j.StartedAt ?? now)
                    .SetProperty(j => j.CompletedAt, (DateTimeOffset?)null),
                cancellationToken);
        return updated > 0;
    }

    public Task RecordAttemptFailureAsync(Guid jobId, string error, CancellationToken cancellationToken) =>
        UpdateUnfinishedAsync(jobId, set => set.SetProperty(j => j.Error, Truncate(error)), cancellationToken);

    public Task MarkCompletedAsync(Guid jobId, int chunkCount, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return UpdateUnfinishedAsync(
            jobId,
            set => set
                .SetProperty(j => j.Status, IngestionStatus.Completed)
                .SetProperty(j => j.ChunkCount, chunkCount)
                .SetProperty(j => j.Error, (string?)null)
                .SetProperty(j => j.CompletedAt, now),
            cancellationToken);
    }

    public Task MarkFailedAsync(Guid jobId, string error, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return UpdateUnfinishedAsync(
            jobId,
            set => set
                .SetProperty(j => j.Status, IngestionStatus.Failed)
                .SetProperty(j => j.Error, Truncate(error))
                .SetProperty(j => j.CompletedAt, now),
            cancellationToken);
    }

    public Task MarkDuplicateAsync(Guid jobId, Guid existingDocumentId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return UpdateUnfinishedAsync(
            jobId,
            set => set
                .SetProperty(j => j.Status, IngestionStatus.Duplicate)
                .SetProperty(j => j.DuplicateOfDocumentId, existingDocumentId)
                .SetProperty(j => j.Error, (string?)null)
                .SetProperty(j => j.CompletedAt, now),
            cancellationToken);
    }

    public async Task<IngestionJob?> FindIndexedDuplicateAsync(string tenantId, string contentHash, Guid excludingJobId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.IngestionJobs.AsNoTracking()
            .Where(j => j.TenantId == tenantId &&
                        j.ContentHash == contentHash &&
                        j.Id != excludingJobId &&
                        j.Status == IngestionStatus.Completed &&
                        j.DocumentRemovedAt == null)
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return entity?.ToJob();
    }

    public async Task MarkDocumentRemovedAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.IngestionJobs
            .Where(j => j.DocumentId == documentId && j.DocumentRemovedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(j => j.DocumentRemovedAt, now), cancellationToken);
    }

    public async Task DeleteAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.IngestionJobs.Where(j => j.Id == jobId).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task UpdateUnfinishedAsync(
        Guid jobId,
        System.Linq.Expressions.Expression<Func<Microsoft.EntityFrameworkCore.Query.SetPropertyCalls<IngestionJobEntity>, Microsoft.EntityFrameworkCore.Query.SetPropertyCalls<IngestionJobEntity>>> set,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.IngestionJobs
            .Where(j => j.Id == jobId && j.Status != IngestionStatus.Completed && j.Status != IngestionStatus.Duplicate)
            .ExecuteUpdateAsync(set, cancellationToken);
    }

    private static string Truncate(string error) => error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];
}
