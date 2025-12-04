using System.Net.Http.Json;
using DocumentIntelligence.Core.Answering;
using DocumentIntelligence.Core.Search;

namespace DocumentIntelligence.Api.Tests;

public sealed class MetadataApiTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public MetadataApiTests() => _client = _factory.CreateClient();

    [Fact]
    public async Task Uploaded_metadata_is_listed_with_the_document()
    {
        await _client.IngestAsync("policy.md", "Remote work is allowed two days a week.", new() { ["tags"] = "HR, policy", ["source"] = "wiki", ["date"] = "2024-03-01" });

        var document = Assert.Single((await _client.GetFromJsonAsync<List<IndexedDocument>>("/api/documents", ApiFactory.Json))!);

        Assert.Equal(new[] { "hr", "policy" }, document.Tags);
        Assert.Equal("wiki", document.Source);
        Assert.Equal(new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero), document.Date);
    }

    [Fact]
    public async Task Query_filters_restrict_the_sources_used_for_the_answer()
    {
        await _client.IngestAsync("hr.md", "The travel budget is 500 EUR per trip.", new() { ["tags"] = "hr", ["source"] = "wiki" });
        await _client.IngestAsync("eng.md", "The travel budget is 900 EUR per trip.", new() { ["tags"] = "engineering", ["source"] = "portal" });

        var byTag = await AskAsync(new { question = "What is the travel budget?", filters = new { tags = new[] { "engineering" } } });
        var bySource = await AskAsync(new { question = "What is the travel budget?", filters = new { source = "wiki" } });

        Assert.All(byTag.Citations, c => Assert.Equal("eng.md", c.DocumentName));
        Assert.Contains("900 EUR", byTag.Answer);
        Assert.All(bySource.Citations, c => Assert.Equal("hr.md", c.DocumentName));
        Assert.Contains("500 EUR", bySource.Answer);
    }

    [Fact]
    public async Task Date_filters_exclude_documents_outside_the_range()
    {
        await _client.IngestAsync("old.md", "The office closes at 5pm.", new() { ["date"] = "2020-01-01" });

        var result = await AskAsync(new { question = "When does the office close?", filters = new { from = "2023-01-01T00:00:00Z" } });

        Assert.Empty(result.Citations);
    }

    [Fact]
    public async Task Invalid_metadata_is_rejected()
    {
        using var form = ApiClientExtensions.Form("a.md", "text", new() { ["tags"] = "bad tag!", ["date"] = "not a date" });

        using var response = await _client.PostAsync("/api/documents", form);

        await DocumentApiTests.AssertValidationProblemAsync(response, "tags", "Tags must be");
    }

    [Fact]
    public async Task An_inverted_date_range_is_rejected()
    {
        using var response = await _client.PostAsJsonAsync("/api/query", new
        {
            question = "anything",
            filters = new { from = "2024-02-01T00:00:00Z", to = "2024-01-01T00:00:00Z" },
        });

        await DocumentApiTests.AssertValidationProblemAsync(response, "filters.from", "must not be later");
    }

    private async Task<AnswerResult> AskAsync(object body)
    {
        using var response = await _client.PostAsJsonAsync("/api/query", body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AnswerResult>(ApiFactory.Json))!;
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
