using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DocumentIntelligence.Core.Search;

namespace DocumentIntelligence.Core.Answering;

/// <summary>Stores generated answers so repeated questions skip retrieval and generation.</summary>
public interface IQueryResultCache
{
    Task<AnswerResult?> GetAsync(QueryCacheKey key, CancellationToken cancellationToken);

    Task SetAsync(QueryCacheKey key, AnswerResult result, CancellationToken cancellationToken);

    /// <summary>Makes every cached answer of a tenant unreachable, after its documents changed.</summary>
    Task InvalidateTenantAsync(string tenantId, CancellationToken cancellationToken);
}

/// <summary>
/// Identifies a cached answer: the tenant plus a digest of the normalised question (trimmed,
/// lower-cased, whitespace collapsed), the requested top-k and the canonical filter.
/// </summary>
public sealed partial record QueryCacheKey(string TenantId, string Digest)
{
    public static QueryCacheKey Create(string question, int topK, SearchFilter filter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(filter);

        var normalized = Whitespace().Replace(question.Trim(), " ").ToLowerInvariant();
        var material = $"{normalized}\n{topK}\n{filter.ToCanonicalString()}";
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        return new QueryCacheKey(filter.TenantId, digest);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>A cache that never holds anything, for deployments that disable caching.</summary>
public sealed class NoQueryResultCache : IQueryResultCache
{
    public Task<AnswerResult?> GetAsync(QueryCacheKey key, CancellationToken cancellationToken) => Task.FromResult<AnswerResult?>(null);

    public Task SetAsync(QueryCacheKey key, AnswerResult result, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task InvalidateTenantAsync(string tenantId, CancellationToken cancellationToken) => Task.CompletedTask;
}
