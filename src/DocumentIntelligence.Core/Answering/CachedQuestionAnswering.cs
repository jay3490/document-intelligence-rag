using System.Diagnostics;
using DocumentIntelligence.Core.Search;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Core.Answering;

/// <summary>
/// Answers questions through <see cref="QuestionAnsweringService"/>, serving repeated questions
/// (same tenant, normalised question, top-k and filter) from <see cref="IQueryResultCache"/>.
/// </summary>
public sealed class CachedQuestionAnswering(
    QuestionAnsweringService answering,
    IQueryResultCache cache,
    IOptions<RetrievalOptions> options)
{
    public async Task<CachedAnswer> AnswerAsync(string question, int? topK, SearchFilter filter, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(filter);

        var started = Stopwatch.GetTimestamp();
        var key = QueryCacheKey.Create(question, topK ?? options.Value.DefaultTopK, filter);
        if (await cache.GetAsync(key, cancellationToken) is { } cached)
        {
            Record(started, hit: true);
            return new CachedAnswer(cached, FromCache: true);
        }

        var result = await answering.AnswerAsync(question, topK, filter, cancellationToken);

        // "Nothing indexed yet" is not worth remembering: the next upload changes it.
        if (result.Citations.Count > 0 || result.Answer != QuestionAnsweringService.NothingIndexedAnswer)
        {
            await cache.SetAsync(key, result, cancellationToken);
        }

        Record(started, hit: false);
        return new CachedAnswer(result, FromCache: false);
    }

    private static void Record(long started, bool hit)
    {
        var tag = new KeyValuePair<string, object?>("cache", hit ? "hit" : "miss");
        Telemetry.QueryDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tag);
        Telemetry.QueryCacheLookups.Add(1, new KeyValuePair<string, object?>("result", hit ? "hit" : "miss"));
    }
}

public sealed record CachedAnswer(AnswerResult Result, bool FromCache);
