using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;

namespace DocumentIntelligence.Api.Tests;

public sealed class ProtectionApiTests : IDisposable
{
    private readonly ApiFactory _factory = new();

    [Fact]
    public async Task Queries_beyond_the_tenant_limit_get_429_with_retry_after()
    {
        using var factory = WithSettings(("RateLimiting:Enabled", "true"), ("RateLimiting:QueriesPerMinute", "2"));
        using var acme = factory.CreateClientFor("acme");
        using var globex = factory.CreateClientFor("globex");

        for (var i = 0; i < 2; i++)
        {
            (await acme.PostAsJsonAsync("/api/query", new { question = "anything?" })).EnsureSuccessStatusCode();
        }

        using var limited = await acme.PostAsJsonAsync("/api/query", new { question = "anything?" });

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter is not null);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);

        // Limits are per tenant.
        (await globex.PostAsJsonAsync("/api/query", new { question = "anything?" })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Health_probes_are_never_rate_limited()
    {
        using var factory = WithSettings(("RateLimiting:Enabled", "true"), ("RateLimiting:RequestsPerMinute", "1"));
        using var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        }
    }

    [Fact]
    public async Task Uploads_larger_than_one_document_are_refused_with_413()
    {
        using var factory = WithSettings(("Ingestion:MaxDocumentBytes", "1024"));
        using var client = factory.CreateClient();

        using var response = await client.UploadAsync("big.txt", new string('a', 200_000));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Json_bodies_above_the_general_limit_are_refused()
    {
        using var factory = WithSettings(("RequestLimits:MaxRequestBodyBytes", "2048"));
        using var client = factory.CreateClient();
        var body = new StringContent($$"""{"question":"{{new string('a', 10_000)}}"}""", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/query", body);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Cors_preflight_allows_only_configured_origins()
    {
        using var factory = WithSettings(("Cors:AllowedOrigins:0", "http://localhost:4200"));
        using var client = factory.CreateClient();

        var allowed = await PreflightAsync(client, "http://localhost:4200");
        var other = await PreflightAsync(client, "http://evil.example");

        Assert.Equal("http://localhost:4200", allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(other.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private static Task<HttpResponseMessage> PreflightAsync(HttpClient client, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/query");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        return client.SendAsync(request);
    }

    private ApiFactory.Configured WithSettings(params (string Key, string Value)[] settings) =>
        _factory.With(builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });

    public void Dispose() => _factory.Dispose();
}
