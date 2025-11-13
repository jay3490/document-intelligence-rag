using System.Collections.Concurrent;

namespace DocumentIntelligence.Api.Ingestion;

/// <summary>
/// Tracks ingestion jobs in memory. Job history is per process and is lost on restart; the
/// ingested documents themselves live in the vector store. Once more than
/// <see cref="MaxRetainedJobs"/> jobs exist, the oldest finished ones are discarded.
/// </summary>
public sealed class IngestionJobStore(TimeProvider timeProvider)
{
    public const int MaxRetainedJobs = 500;

    private readonly ConcurrentDictionary<Guid, IngestionJob> _jobs = new();

    public IngestionJob Create(Guid documentId, string fileName, long sizeBytes)
    {
        var job = new IngestionJob(
            Guid.NewGuid(), documentId, fileName, sizeBytes, IngestionStatus.Queued,
            ChunkCount: null, Error: null, timeProvider.GetUtcNow(), CompletedAt: null);

        _jobs[job.Id] = job;
        Trim();
        return job;
    }

    public IngestionJob? Get(Guid id) => _jobs.GetValueOrDefault(id);

    /// <summary>All retained jobs, newest first.</summary>
    public IReadOnlyList<IngestionJob> List() =>
        _jobs.Values.OrderByDescending(job => job.CreatedAt).ThenBy(job => job.Id).ToList();

    public void Remove(Guid id) => _jobs.TryRemove(id, out _);

    public void MarkProcessing(Guid id) =>
        Update(id, job => job with { Status = IngestionStatus.Processing });

    public void MarkCompleted(Guid id, int chunkCount) =>
        Update(id, job => job with { Status = IngestionStatus.Completed, ChunkCount = chunkCount, CompletedAt = timeProvider.GetUtcNow() });

    public void MarkFailed(Guid id, string error) =>
        Update(id, job => job with { Status = IngestionStatus.Failed, Error = error, CompletedAt = timeProvider.GetUtcNow() });

    private void Update(Guid id, Func<IngestionJob, IngestionJob> change)
    {
        while (_jobs.TryGetValue(id, out var current))
        {
            if (_jobs.TryUpdate(id, change(current), current))
            {
                return;
            }
        }
    }

    private void Trim()
    {
        var excess = _jobs.Count - MaxRetainedJobs;
        if (excess <= 0)
        {
            return;
        }

        var oldestFinished = _jobs.Values
            .Where(job => job.Status is IngestionStatus.Completed or IngestionStatus.Failed)
            .OrderBy(job => job.CreatedAt)
            .Take(excess);

        foreach (var job in oldestFinished)
        {
            _jobs.TryRemove(job.Id, out _);
        }
    }
}
