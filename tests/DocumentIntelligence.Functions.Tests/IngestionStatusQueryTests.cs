using DocumentIntelligence.Core.Ingestion;
using DocumentIntelligence.Functions.Drops;
using DocumentIntelligence.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DocumentIntelligence.Functions.Tests;

public sealed class IngestionStatusQueryTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private EfIngestionJobStore _jobs = null!;
    private IngestionStatusQuery _query = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var factory = new ContextFactory(new DbContextOptionsBuilder<IngestionDbContext>().UseSqlite(_connection).Options);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.EnsureCreatedAsync();
        }

        _jobs = new EfIngestionJobStore(factory, TimeProvider.System);
        _query = new IngestionStatusQuery(_jobs);
    }

    public Task DisposeAsync() => _connection.DisposeAsync().AsTask();

    [Fact]
    public async Task Returns_a_job_of_the_requested_tenant()
    {
        var job = await AddJobAsync("acme");

        var (result, found) = await _query.GetAsync("acme", job.Id, CancellationToken.None);

        Assert.Equal(StatusQueryResult.Found, result);
        Assert.Equal(job.Id, found!.Id);
    }

    [Fact]
    public async Task Another_tenants_job_is_not_found()
    {
        var job = await AddJobAsync("acme");

        var (result, found) = await _query.GetAsync("globex", job.Id, CancellationToken.None);

        Assert.Equal(StatusQueryResult.NotFound, result);
        Assert.Null(found);
    }

    [Fact]
    public async Task Lists_the_tenants_jobs_with_a_clamped_limit()
    {
        for (var i = 0; i < 3; i++)
        {
            await AddJobAsync("acme");
        }

        await AddJobAsync("globex");

        Assert.Equal(3, (await _query.ListAsync("acme", null, CancellationToken.None)).Jobs.Count);
        Assert.Single((await _query.ListAsync("acme", 0, CancellationToken.None)).Jobs);
        Assert.Equal(2, (await _query.ListAsync("acme", 2, CancellationToken.None)).Jobs.Count);
    }

    [Fact]
    public async Task Invalid_tenant_ids_are_refused()
    {
        Assert.Equal(StatusQueryResult.InvalidTenant, (await _query.ListAsync("../etc", null, CancellationToken.None)).Result);
        Assert.Equal(StatusQueryResult.InvalidTenant, (await _query.GetAsync("ACME", Guid.NewGuid(), CancellationToken.None)).Result);
    }

    [Fact]
    public void Blob_metadata_is_read_from_trigger_binding_data()
    {
        var fromJson = BlobTriggerMetadata.Read(new Dictionary<string, object?> { ["Metadata"] = """{"tags":"hr","source":"wiki"}""" });
        var missing = BlobTriggerMetadata.Read(new Dictionary<string, object?>());
        var malformed = BlobTriggerMetadata.Read(new Dictionary<string, object?> { ["Metadata"] = "{oops" });

        Assert.Equal("wiki", fromJson["source"]);
        Assert.Empty(missing);
        Assert.Empty(malformed);
    }

    private async Task<IngestionJob> AddJobAsync(string tenant) =>
        await _jobs.AddAsync(
            new IngestionJob
            {
                Id = Guid.NewGuid(),
                TenantId = tenant,
                DocumentId = Guid.NewGuid(),
                FileName = "a.md",
                SizeBytes = 1,
                ContentHash = "aa",
                BlobName = $"{tenant}/a.md",
                CreatedAt = DateTimeOffset.UtcNow,
            },
            CancellationToken.None);

    private sealed class ContextFactory(DbContextOptions<IngestionDbContext> options) : IDbContextFactory<IngestionDbContext>
    {
        public IngestionDbContext CreateDbContext() => new(options);
    }
}
