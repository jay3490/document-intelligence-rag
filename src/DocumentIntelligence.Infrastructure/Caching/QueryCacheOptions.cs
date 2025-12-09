using System.ComponentModel.DataAnnotations;

namespace DocumentIntelligence.Infrastructure.Caching;

public enum QueryCacheProvider
{
    /// <summary>Caching is off; every question is answered afresh.</summary>
    None,

    /// <summary>A per-process cache; replicas do not share entries.</summary>
    Memory,

    /// <summary>A Redis server shared by every API replica.</summary>
    Redis,
}

public sealed class QueryCacheOptions : IValidatableObject
{
    public const string SectionName = "QueryCache";

    public QueryCacheProvider Provider { get; set; } = QueryCacheProvider.Memory;

    /// <summary>A StackExchange.Redis configuration string, for example "redis:6379".</summary>
    public string? RedisConnection { get; set; }

    /// <summary>Prefix for every key, so several deployments can share one Redis.</summary>
    [Required]
    public string KeyPrefix { get; set; } = "di";

    /// <summary>How long an answer stays cached.</summary>
    [Range(1, 86_400)]
    public int TtlSeconds { get; set; } = 300;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Provider == QueryCacheProvider.Redis && string.IsNullOrWhiteSpace(RedisConnection))
        {
            yield return new ValidationResult($"{nameof(RedisConnection)} is required for the Redis provider.", [nameof(RedisConnection)]);
        }
    }
}
