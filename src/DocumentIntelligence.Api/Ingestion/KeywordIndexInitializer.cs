using DocumentIntelligence.Core.Documents;
using DocumentIntelligence.Core.Ingestion;
using DocumentIntelligence.Core.Search;
using DocumentIntelligence.Core.Storage;

namespace DocumentIntelligence.Api.Ingestion;

/// <summary>
/// Rebuilds the in-memory BM25 index from the vector store when the service starts, so keyword
/// search covers documents ingested by earlier runs. Retries until the store is reachable.
/// </summary>
public sealed partial class KeywordIndexInitializer(
    IVectorStore vectorStore,
    Bm25Index keywordIndex,
    ILogger<KeywordIndexInitializer> logger) : BackgroundService, IIngestionReadiness
{
    private const int BatchSize = 512;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsReady => _ready.Task.IsCompletedSuccessfully;

    /// <summary>Completes once the index has been rebuilt.</summary>
    public Task WaitUntilReadyAsync(CancellationToken cancellationToken) => _ready.Task.WaitAsync(cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var loaded = await LoadAsync(stoppingToken);
                LogLoaded(logger, loaded);
                _ready.TrySetResult();
                return;
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogLoadFailed(logger, exception, RetryDelay.TotalSeconds);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }

    private async Task<int> LoadAsync(CancellationToken cancellationToken)
    {
        var batch = new List<DocumentChunk>(BatchSize);
        var loaded = 0;

        await foreach (var chunk in vectorStore.ReadAllAsync(cancellationToken))
        {
            batch.Add(chunk);
            if (batch.Count == BatchSize)
            {
                keywordIndex.Add(batch);
                loaded += batch.Count;
                batch.Clear();
            }
        }

        keywordIndex.Add(batch);
        return loaded + batch.Count;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Keyword index rebuilt from the vector store with {ChunkCount} chunks")]
    private static partial void LogLoaded(ILogger logger, int chunkCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not rebuild the keyword index; retrying in {DelaySeconds}s")]
    private static partial void LogLoadFailed(ILogger logger, Exception exception, double delaySeconds);
}
