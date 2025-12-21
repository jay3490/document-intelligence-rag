using DocumentIntelligence.Core.Ingestion;
using DocumentIntelligence.Core.Tenancy;

namespace DocumentIntelligence.Functions.Drops;

public enum StatusQueryResult
{
    Found,
    NotFound,
    InvalidTenant,
}

/// <summary>Reads ingestion job status for the HTTP status function, scoped to one tenant.</summary>
public sealed class IngestionStatusQuery(IIngestionJobStore jobs)
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    /// <summary>A job of the tenant; another tenant's job is reported as not found.</summary>
    public async Task<(StatusQueryResult Result, IngestionJob? Job)> GetAsync(string tenant, Guid jobId, CancellationToken cancellationToken)
    {
        if (!TenantId.IsValid(tenant))
        {
            return (StatusQueryResult.InvalidTenant, null);
        }

        var job = await jobs.GetAsync(jobId, cancellationToken);
        return job is not null && job.TenantId == tenant ? (StatusQueryResult.Found, job) : (StatusQueryResult.NotFound, null);
    }

    /// <summary>The tenant's most recent jobs, newest first; the limit is clamped to 1..100.</summary>
    public async Task<(StatusQueryResult Result, IReadOnlyList<IngestionJob> Jobs)> ListAsync(string tenant, int? limit, CancellationToken cancellationToken)
    {
        if (!TenantId.IsValid(tenant))
        {
            return (StatusQueryResult.InvalidTenant, []);
        }

        var recent = await jobs.ListAsync(tenant, Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit), cancellationToken);
        return (StatusQueryResult.Found, recent);
    }
}
