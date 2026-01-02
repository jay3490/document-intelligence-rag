using System.Net;
using System.Net.Http.Json;
using DocumentIntelligence.Api.Hosting;
using DocumentIntelligence.Core.Ingestion;

namespace DocumentIntelligence.Api.Tests;

public sealed class ObservabilityApiTests : IDisposable
{
    private readonly ApiFactory _factory = new();

    [Fact]
    public async Task A_supplied_correlation_id_is_echoed_and_stored_on_the_job()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(CorrelationIds.HeaderName, "upload-123");

        using var response = await client.UploadAsync("a.md", "Some text.");
        var job = await response.Content.ReadFromJsonAsync<IngestionJob>(ApiFactory.Json);

        Assert.Equal("upload-123", response.Headers.GetValues(CorrelationIds.HeaderName).Single());
        Assert.Equal("upload-123", job!.CorrelationId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has spaces and <angle> brackets")]
    public async Task Missing_or_malformed_correlation_ids_are_replaced(string supplied)
    {
        using var client = _factory.CreateClient();
        if (supplied.Length > 0)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(CorrelationIds.HeaderName, supplied);
        }

        using var response = await client.GetAsync("/health/live");

        var echoed = response.Headers.GetValues(CorrelationIds.HeaderName).Single();
        Assert.NotEqual(supplied, echoed);
        Assert.Matches("^[A-Za-z0-9_.:-]{1,128}$", echoed);
    }

    [Fact]
    public async Task Metrics_are_exposed_for_prometheus_without_a_token()
    {
        using var client = _factory.CreateClient();
        await client.IngestAsync("a.md", "Lunch is served at noon.");
        (await client.PostAsJsonAsync("/api/query", new { question = "When is lunch?" })).EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = null;

        // The exporter caches its scrape briefly; poll until the query shows up.
        string body = string.Empty;
        for (var attempt = 0; attempt < 40 && !body.Contains("di_query_duration_seconds", StringComparison.Ordinal); attempt++)
        {
            using var response = await client.GetAsync(ObservabilityExtensions.MetricsPath);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            body = await response.Content.ReadAsStringAsync();
            await Task.Delay(100);
        }

        Assert.Contains("di_query_duration_seconds", body);
        Assert.Contains("di_ingestion_chunks", body);
        Assert.Contains("di_ingestion_lag_seconds", body);
        Assert.Contains("di_query_cache_lookups", body);
    }

    public void Dispose() => _factory.Dispose();
}
