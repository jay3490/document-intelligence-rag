using System.Net;
using System.Text.Json;

namespace DocumentIntelligence.Api.Tests;

public sealed class HealthApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public HealthApiTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Liveness_runs_no_checks()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(0, body.RootElement.GetProperty("checks").GetArrayLength());
    }

    [Fact]
    public async Task Readiness_reports_the_vector_store_and_keyword_index()
    {
        using var client = _factory.CreateClient();

        // The keyword index is rebuilt in the background right after start-up.
        HttpResponseMessage response;
        var attempts = 0;
        do
        {
            response = await client.GetAsync("/health/ready");
            if (response.StatusCode == HttpStatusCode.OK || ++attempts == 100)
            {
                break;
            }

            response.Dispose();
            await Task.Delay(50);
        }
        while (true);

        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(
                new[] { "keyword-index", "vector-store" },
                body.RootElement.GetProperty("checks").EnumerateArray().Select(c => c.GetProperty("name").GetString()).Order());
        }
    }
}
