using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Infrastructure.Persistence;

/// <summary>
/// Prepares the job database before the host starts serving: migrations on PostgreSQL, schema
/// creation on SQLite. A shared in-memory SQLite database lives only while a connection to it is
/// open, so one is held for the lifetime of the host.
/// </summary>
public sealed partial class DatabaseInitializer(
    IDbContextFactory<IngestionDbContext> contextFactory,
    IOptions<PersistenceOptions> options,
    ILogger<DatabaseInitializer> logger) : IHostedService, IAsyncDisposable
{
    private const int MaxAttempts = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    private SqliteConnection? _keepAlive;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (settings.Provider == DatabaseProvider.Sqlite)
        {
            await PrepareSqliteAsync(settings.ConnectionString, cancellationToken);
        }

        if (!settings.InitializeOnStartup)
        {
            return;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
                if (settings.Provider == DatabaseProvider.Postgres)
                {
                    await context.Database.MigrateAsync(cancellationToken);
                }
                else
                {
                    await context.Database.EnsureCreatedAsync(cancellationToken);
                }

                LogInitialized(logger, settings.Provider);
                return;
            }
            catch (Exception exception) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested && exception is not InvalidOperationException)
            {
                LogRetrying(logger, exception, attempt, RetryDelay.TotalSeconds);
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_keepAlive is not null)
        {
            await _keepAlive.DisposeAsync();
        }
    }

    private async Task PrepareSqliteAsync(string connectionString, CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (builder.Mode == SqliteOpenMode.Memory || builder.DataSource.Contains("mode=memory", StringComparison.OrdinalIgnoreCase))
        {
            _keepAlive = new SqliteConnection(connectionString);
            await _keepAlive.OpenAsync(cancellationToken);
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(builder.DataSource));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ingestion job database ready ({Provider})")]
    private static partial void LogInitialized(ILogger logger, DatabaseProvider provider);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ingestion job database not ready (attempt {Attempt}); retrying in {DelaySeconds}s")]
    private static partial void LogRetrying(ILogger logger, Exception exception, int attempt, double delaySeconds);
}
