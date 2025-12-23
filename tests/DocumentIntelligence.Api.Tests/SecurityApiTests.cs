using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DocumentIntelligence.Api.Security;
using DocumentIntelligence.Core.Answering;
using DocumentIntelligence.Core.Ingestion;
using DocumentIntelligence.Core.Search;

namespace DocumentIntelligence.Api.Tests;

public sealed class SecurityApiTests : IDisposable
{
    private readonly ApiFactory _factory = new();

    [Theory]
    [InlineData("GET", "/api/documents")]
    [InlineData("GET", "/api/ingestions")]
    [InlineData("POST", "/api/query")]
    public async Task Requests_without_a_token_are_rejected(string method, string path)
    {
        using var client = AnonymousClient();

        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { question = "q" }) });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_probes_stay_anonymous()
    {
        using var client = AnonymousClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task Tokens_signed_with_another_key_are_rejected()
    {
        using var client = AnonymousClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ0ZW5hbnRfaWQiOiJhY21lIn0.c2lnbmF0dXJlLW5vdC12YWxpZA");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/documents")).StatusCode);
    }

    [Fact]
    public async Task The_dev_token_endpoint_issues_a_usable_token()
    {
        using var client = AnonymousClient();

        using var response = await client.PostAsJsonAsync("/api/auth/dev-token", new { tenant = "acme", user = "alice" });
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<DevToken>(ApiFactory.Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        Assert.Equal("acme", token.TenantId);
        Assert.True(token.ExpiresAt > DateTimeOffset.UtcNow);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/documents")).StatusCode);
    }

    [Fact]
    public async Task The_dev_token_endpoint_validates_the_tenant()
    {
        using var client = AnonymousClient();

        using var response = await client.PostAsJsonAsync("/api/auth/dev-token", new { tenant = "Not Valid" });

        await DocumentApiTests.AssertValidationProblemAsync(response, "tenant", "lower-case");
    }

    [Fact]
    public async Task Tenants_cannot_see_or_change_each_others_documents()
    {
        using var acme = _factory.CreateClientFor("acme");
        using var globex = _factory.CreateClientFor("globex");
        var job = await acme.IngestAsync("secret.md", "The acme launch code is 4242.");

        Assert.Empty((await globex.GetFromJsonAsync<List<IndexedDocument>>("/api/documents", ApiFactory.Json))!);
        Assert.Empty((await globex.GetFromJsonAsync<List<IngestionJob>>("/api/ingestions", ApiFactory.Json))!);
        Assert.Equal(HttpStatusCode.NotFound, (await globex.GetAsync($"/api/ingestions/{job.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await globex.DeleteAsync($"/api/documents/{job.DocumentId}")).StatusCode);

        var answer = await (await globex.PostAsJsonAsync("/api/query", new { question = "What is the acme launch code?" }))
            .Content.ReadFromJsonAsync<AnswerResult>(ApiFactory.Json);
        Assert.Empty(answer!.Citations);
        Assert.DoesNotContain("4242", answer.Answer);

        var own = await (await acme.PostAsJsonAsync("/api/query", new { question = "What is the acme launch code?" }))
            .Content.ReadFromJsonAsync<AnswerResult>(ApiFactory.Json);
        Assert.Contains("4242", own!.Answer);
        Assert.Single((await acme.GetFromJsonAsync<List<IndexedDocument>>("/api/documents", ApiFactory.Json))!);
    }

    [Fact]
    public void Jwt_mode_needs_an_authority_and_audience()
    {
        var options = new AuthOptions { Mode = AuthMode.Jwt };
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        Assert.False(System.ComponentModel.DataAnnotations.Validator.TryValidateObject(options, new(options), results, validateAllProperties: true));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AuthOptions.Authority)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AuthOptions.Audience)));
    }

    [Fact]
    public void Dev_token_mode_needs_a_long_enough_key()
    {
        var options = new AuthOptions { Mode = AuthMode.DevToken, DevToken = { SigningKey = "short" } };
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        Assert.False(System.ComponentModel.DataAnnotations.Validator.TryValidateObject(options, new(options), results, validateAllProperties: true));
    }

    private HttpClient AnonymousClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        return client;
    }

    public void Dispose() => _factory.Dispose();
}
