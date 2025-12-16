using System.Collections.Concurrent;
using DocumentIntelligence.Core.Answering;
using DocumentIntelligence.Core.Messaging;
using DocumentIntelligence.Core.Storage;
using DocumentIntelligence.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DocumentIntelligence.UnitTests.TestDoubles;

/// <summary>A job store on a private in-memory SQLite database.</summary>
internal sealed class SqliteJobDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private SqliteJobDatabase(SqliteConnection connection, IDbContextFactory<IngestionDbContext> factory)
    {
        _connection = connection;
        Factory = factory;
    }

    public IDbContextFactory<IngestionDbContext> Factory { get; }

    public static async Task<SqliteJobDatabase> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var factory = new ContextFactory(new DbContextOptionsBuilder<IngestionDbContext>().UseSqlite(connection).Options);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.EnsureCreatedAsync();
        }

        return new SqliteJobDatabase(connection, factory);
    }

    public EfIngestionJobStore CreateStore(TimeProvider? time = null) => new(Factory, time ?? TimeProvider.System);

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    private sealed class ContextFactory(DbContextOptions<IngestionDbContext> options) : IDbContextFactory<IngestionDbContext>
    {
        public IngestionDbContext CreateDbContext() => new(options);
    }
}

internal sealed class MemoryBlobStore : IBlobStore
{
    public ConcurrentDictionary<string, byte[]> Blobs { get; } = new();

    public async Task PutAsync(string name, Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        Blobs[name] = buffer.ToArray();
    }

    public Task<Stream?> OpenReadAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult<Stream?>(Blobs.TryGetValue(name, out var bytes) ? new MemoryStream(bytes, writable: false) : null);

    public Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        Blobs.TryRemove(name, out _);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingPublisher : IIngestionEventPublisher
{
    public ConcurrentQueue<DocumentUploaded> Uploaded { get; } = new();

    public ConcurrentQueue<DeadLetteredUpload> DeadLetters { get; } = new();

    public ConcurrentQueue<DocumentIndexChanged> IndexChanges { get; } = new();

    public Task PublishUploadedAsync(DocumentUploaded message, CancellationToken cancellationToken)
    {
        Uploaded.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task PublishDeadLetterAsync(DeadLetteredUpload message, CancellationToken cancellationToken)
    {
        DeadLetters.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task PublishIndexChangedAsync(DocumentIndexChanged message, CancellationToken cancellationToken)
    {
        IndexChanges.Enqueue(message);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingQueryCache : IQueryResultCache
{
    public List<string> InvalidatedTenants { get; } = [];

    public Task<AnswerResult?> GetAsync(QueryCacheKey key, CancellationToken cancellationToken) => Task.FromResult<AnswerResult?>(null);

    public Task SetAsync(QueryCacheKey key, AnswerResult result, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task InvalidateTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        InvalidatedTenants.Add(tenantId);
        return Task.CompletedTask;
    }
}
