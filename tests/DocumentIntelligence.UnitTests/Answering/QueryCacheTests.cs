using DocumentIntelligence.Core.Answering;
using DocumentIntelligence.Core.Search;
using DocumentIntelligence.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.UnitTests.Answering;

public sealed class QueryCacheTests
{
    private static readonly AnswerResult Answer = new(
        "25 days [1].",
        [new Citation(1, Guid.NewGuid(), Guid.NewGuid(), "leave.md", "Leave", 0, "Vacation is 25 days.", 0.03)]);

    [Fact]
    public void Keys_ignore_case_and_whitespace_but_not_filters_or_top_k()
    {
        var filter = new SearchFilter("acme") { Tags = ["hr"] };
        var key = QueryCacheKey.Create("How many  vacation days?", 5, filter);

        Assert.Equal(key, QueryCacheKey.Create("  how many vacation\tdays? ", 5, new SearchFilter("acme") { Tags = ["HR"] }));
        Assert.NotEqual(key, QueryCacheKey.Create("How many vacation days?", 4, filter));
        Assert.NotEqual(key, QueryCacheKey.Create("How many vacation days?", 5, new SearchFilter("acme")));
        Assert.NotEqual(key.Digest, QueryCacheKey.Create("How many vacation days?", 5, new SearchFilter("globex") { Tags = ["hr"] }).Digest);
    }

    [Fact]
    public async Task Round_trips_answers()
    {
        var cache = CreateCache();
        var key = QueryCacheKey.Create("q", 5, new SearchFilter("acme"));

        await cache.SetAsync(key, Answer, CancellationToken.None);
        var cached = await cache.GetAsync(key, CancellationToken.None);

        Assert.NotNull(cached);
        Assert.Equal(Answer.Answer, cached.Answer);
        Assert.Equal(Answer.Citations, cached.Citations);
    }

    [Fact]
    public async Task Invalidating_a_tenant_drops_only_its_answers()
    {
        var cache = CreateCache();
        var acme = QueryCacheKey.Create("q", 5, new SearchFilter("acme"));
        var globex = QueryCacheKey.Create("q", 5, new SearchFilter("globex"));
        await cache.SetAsync(acme, Answer, CancellationToken.None);
        await cache.SetAsync(globex, Answer, CancellationToken.None);

        await cache.InvalidateTenantAsync("acme", CancellationToken.None);

        Assert.Null(await cache.GetAsync(acme, CancellationToken.None));
        Assert.NotNull(await cache.GetAsync(globex, CancellationToken.None));
    }

    [Fact]
    public async Task A_failing_backing_store_behaves_like_an_empty_cache()
    {
        var cache = new DistributedQueryResultCache(new BrokenCache(), Options.Create(new QueryCacheOptions()), NullLogger<DistributedQueryResultCache>.Instance);
        var key = QueryCacheKey.Create("q", 5, new SearchFilter("acme"));

        await cache.SetAsync(key, Answer, CancellationToken.None);
        await cache.InvalidateTenantAsync("acme", CancellationToken.None);

        Assert.Null(await cache.GetAsync(key, CancellationToken.None));
    }

    [Fact]
    public void Redis_needs_a_connection_string()
    {
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var options = new QueryCacheOptions { Provider = QueryCacheProvider.Redis };

        Assert.False(System.ComponentModel.DataAnnotations.Validator.TryValidateObject(options, new(options), results, validateAllProperties: true));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(QueryCacheOptions.RedisConnection)));
    }

    private static DistributedQueryResultCache CreateCache() =>
        new(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Options.Create(new QueryCacheOptions()),
            NullLogger<DistributedQueryResultCache>.Instance);

    private sealed class BrokenCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new InvalidOperationException("redis down");

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("redis down");

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new InvalidOperationException("redis down");

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
            throw new InvalidOperationException("redis down");

        public void Refresh(string key) => throw new InvalidOperationException("redis down");

        public Task RefreshAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("redis down");

        public void Remove(string key) => throw new InvalidOperationException("redis down");

        public Task RemoveAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("redis down");
    }
}
