using System.Net.Http.Json;
using DocumentIntelligence.Api.Endpoints;

namespace DocumentIntelligence.Api.Tests;

public sealed class QueryCacheApiTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public QueryCacheApiTests() => _client = _factory.CreateClient();

    [Fact]
    public async Task A_repeated_question_is_served_from_the_cache_until_documents_change()
    {
        var first = await _client.IngestAsync("a.md", "Lunch is served at noon.");

        Assert.Equal("miss", await AskAsync("When is lunch served?"));
        Assert.Equal("hit", await AskAsync("  when is LUNCH served? "));

        await _client.IngestAsync("b.md", "Dinner is served at seven.");
        Assert.Equal("miss", await AskAsync("When is lunch served?"));
        Assert.Equal("hit", await AskAsync("When is lunch served?"));

        (await _client.DeleteAsync($"/api/documents/{first.DocumentId}")).EnsureSuccessStatusCode();
        Assert.Equal("miss", await AskAsync("When is lunch served?"));
    }

    private async Task<string?> AskAsync(string question)
    {
        using var response = await _client.PostAsJsonAsync("/api/query", new { question });
        response.EnsureSuccessStatusCode();
        return response.Headers.GetValues(QueryEndpoints.CacheHeader).SingleOrDefault();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
