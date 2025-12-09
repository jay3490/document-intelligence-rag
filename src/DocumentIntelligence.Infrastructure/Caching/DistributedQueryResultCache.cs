using System.Text.Json;
using DocumentIntelligence.Core.Answering;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Infrastructure.Caching;

/// <summary>
/// Caches answers in an <see cref="IDistributedCache"/> (Redis or process memory).
/// </summary>
/// <remarks>
/// Each tenant has a generation token stored next to its answers, and the token is part of every
/// answer key. Invalidating a tenant replaces the token, which orphans all of its cached
/// answers at once; they then expire on their own. The cache is never allowed to fail a query:
/// errors are logged and treated as misses.
/// </remarks>
public sealed partial class DistributedQueryResultCache(
    IDistributedCache cache,
    IOptions<QueryCacheOptions> options,
    ILogger<DistributedQueryResultCache> logger) : IQueryResultCache
{
    private const string InitialGeneration = "0";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AnswerResult?> GetAsync(QueryCacheKey key, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await cache.GetAsync(await AnswerKeyAsync(key, cancellationToken), cancellationToken);
            return bytes is null ? null : JsonSerializer.Deserialize<AnswerResult>(bytes, Json);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogCacheFailure(logger, exception, "read");
            return null;
        }
    }

    public async Task SetAsync(QueryCacheKey key, AnswerResult result, CancellationToken cancellationToken)
    {
        try
        {
            var entry = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(options.Value.TtlSeconds) };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(result, Json);
            await cache.SetAsync(await AnswerKeyAsync(key, cancellationToken), bytes, entry, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogCacheFailure(logger, exception, "write");
        }
    }

    public async Task InvalidateTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        try
        {
            await cache.SetStringAsync(GenerationKey(tenantId), Guid.NewGuid().ToString("N"), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogCacheFailure(logger, exception, "invalidate");
        }
    }

    private async Task<string> AnswerKeyAsync(QueryCacheKey key, CancellationToken cancellationToken)
    {
        var generation = await cache.GetStringAsync(GenerationKey(key.TenantId), cancellationToken) ?? InitialGeneration;
        return $"{options.Value.KeyPrefix}:qa:{key.TenantId}:{generation}:{key.Digest}";
    }

    private string GenerationKey(string tenantId) => $"{options.Value.KeyPrefix}:qa:{tenantId}:generation";

    [LoggerMessage(Level = LogLevel.Warning, Message = "Query cache {Operation} failed; continuing without the cache")]
    private static partial void LogCacheFailure(ILogger logger, Exception exception, string operation);
}
