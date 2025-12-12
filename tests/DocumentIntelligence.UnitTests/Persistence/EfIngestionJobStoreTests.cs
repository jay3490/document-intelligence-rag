using DocumentIntelligence.Core.Ingestion;
using DocumentIntelligence.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace DocumentIntelligence.UnitTests.Persistence;

public sealed class EfIngestionJobStoreTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2024, 5, 1, 9, 0, 0, TimeSpan.Zero));
    private EfIngestionJobStore _store = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var factory = new ConnectionFactory(new DbContextOptionsBuilder<IngestionDbContext>().UseSqlite(_connection).Options);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.EnsureCreatedAsync();
        }

        _store = new EfIngestionJobStore(factory, _time);
    }

    public Task DisposeAsync() => _connection.DisposeAsync().AsTask();

    [Fact]
    public async Task Adds_and_reads_back_a_job()
    {
        var job = NewJob("acme");

        await _store.AddAsync(job, CancellationToken.None);

        Assert.Equal(job, await _store.GetAsync(job.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Adding_a_known_job_id_keeps_the_stored_job()
    {
        var job = NewJob("acme");
        await _store.AddAsync(job, CancellationToken.None);
        await _store.TryStartAttemptAsync(job.Id, CancellationToken.None);

        var stored = await _store.AddAsync(job with { FileName = "other.md" }, CancellationToken.None);

        Assert.Equal("doc.md", stored.FileName);
        Assert.Equal(IngestionStatus.Processing, stored.Status);
    }

    [Fact]
    public async Task Lists_only_the_tenants_jobs_newest_first()
    {
        var older = NewJob("acme");
        _time.Advance(TimeSpan.FromMinutes(1));
        var newer = NewJob("acme");
        await _store.AddAsync(older, CancellationToken.None);
        await _store.AddAsync(newer, CancellationToken.None);
        await _store.AddAsync(NewJob("globex"), CancellationToken.None);

        var jobs = await _store.ListAsync("acme", 10, CancellationToken.None);

        Assert.Equal(new[] { newer.Id, older.Id }, jobs.Select(j => j.Id));
        Assert.Single(await _store.ListAsync("acme", 1, CancellationToken.None));
    }

    [Fact]
    public async Task Attempts_are_counted_and_completion_is_recorded()
    {
        var job = NewJob("acme");
        await _store.AddAsync(job, CancellationToken.None);

        Assert.True(await _store.TryStartAttemptAsync(job.Id, CancellationToken.None));
        await _store.RecordAttemptFailureAsync(job.Id, "embeddings timed out", CancellationToken.None);
        Assert.True(await _store.TryStartAttemptAsync(job.Id, CancellationToken.None));
        _time.Advance(TimeSpan.FromSeconds(5));
        await _store.MarkCompletedAsync(job.Id, 7, CancellationToken.None);

        var stored = (await _store.GetAsync(job.Id, CancellationToken.None))!;
        Assert.Equal(IngestionStatus.Completed, stored.Status);
        Assert.Equal(2, stored.Attempts);
        Assert.Equal(7, stored.ChunkCount);
        Assert.Null(stored.Error);
        Assert.Equal(job.CreatedAt, stored.StartedAt);
        Assert.Equal(job.CreatedAt.AddSeconds(5), stored.CompletedAt);
    }

    [Fact]
    public async Task Finished_jobs_cannot_be_started_or_failed_again()
    {
        var job = NewJob("acme");
        await _store.AddAsync(job, CancellationToken.None);
        await _store.TryStartAttemptAsync(job.Id, CancellationToken.None);
        await _store.MarkCompletedAsync(job.Id, 3, CancellationToken.None);

        Assert.False(await _store.TryStartAttemptAsync(job.Id, CancellationToken.None));
        await _store.MarkFailedAsync(job.Id, "late failure", CancellationToken.None);

        Assert.Equal(IngestionStatus.Completed, (await _store.GetAsync(job.Id, CancellationToken.None))!.Status);
        Assert.False(await _store.TryStartAttemptAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task A_failed_job_can_be_retried()
    {
        var job = NewJob("acme");
        await _store.AddAsync(job, CancellationToken.None);
        await _store.MarkFailedAsync(job.Id, new string('x', 5000), CancellationToken.None);

        var failed = (await _store.GetAsync(job.Id, CancellationToken.None))!;
        Assert.Equal(EfIngestionJobStore.MaxErrorLength, failed.Error!.Length);
        Assert.True(await _store.TryStartAttemptAsync(job.Id, CancellationToken.None));
        Assert.Null((await _store.GetAsync(job.Id, CancellationToken.None))!.CompletedAt);
    }

    [Fact]
    public async Task Duplicates_are_found_by_tenant_and_hash_while_the_document_is_indexed()
    {
        var original = NewJob("acme", hash: "aa");
        await _store.AddAsync(original, CancellationToken.None);
        await _store.MarkCompletedAsync(original.Id, 2, CancellationToken.None);
        var copy = NewJob("acme", hash: "aa");

        Assert.Equal(original.Id, (await _store.FindIndexedDuplicateAsync("acme", "aa", copy.Id, CancellationToken.None))?.Id);
        Assert.Null(await _store.FindIndexedDuplicateAsync("globex", "aa", copy.Id, CancellationToken.None));
        Assert.Null(await _store.FindIndexedDuplicateAsync("acme", "aa", original.Id, CancellationToken.None));

        await _store.MarkDocumentRemovedAsync(original.DocumentId, CancellationToken.None);

        Assert.Null(await _store.FindIndexedDuplicateAsync("acme", "aa", copy.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Marking_a_duplicate_records_the_existing_document()
    {
        var job = NewJob("acme");
        var existing = Guid.NewGuid();
        await _store.AddAsync(job, CancellationToken.None);

        await _store.MarkDuplicateAsync(job.Id, existing, CancellationToken.None);

        var stored = (await _store.GetAsync(job.Id, CancellationToken.None))!;
        Assert.Equal(IngestionStatus.Duplicate, stored.Status);
        Assert.Equal(existing, stored.DuplicateOfDocumentId);
        Assert.True(stored.IsFinished);
    }

    [Fact]
    public async Task Deleted_jobs_are_gone()
    {
        var job = NewJob("acme");
        await _store.AddAsync(job, CancellationToken.None);

        await _store.DeleteAsync(job.Id, CancellationToken.None);

        Assert.Null(await _store.GetAsync(job.Id, CancellationToken.None));
    }

    private IngestionJob NewJob(string tenant, string hash = "0123") => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenant,
        DocumentId = Guid.NewGuid(),
        FileName = "doc.md",
        SizeBytes = 42,
        ContentHash = hash,
        BlobName = $"{tenant}/doc.md",
        CorrelationId = "corr-1",
        CreatedAt = _time.GetUtcNow(),
    };

    private sealed class ConnectionFactory(DbContextOptions<IngestionDbContext> options) : IDbContextFactory<IngestionDbContext>
    {
        public IngestionDbContext CreateDbContext() => new(options);
    }
}
