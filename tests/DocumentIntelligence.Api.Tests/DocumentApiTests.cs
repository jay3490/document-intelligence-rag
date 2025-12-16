using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentIntelligence.Core.Ingestion;
using DocumentIntelligence.Core.Search;

namespace DocumentIntelligence.Api.Tests;

public sealed class DocumentApiTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public DocumentApiTests() => _client = _factory.CreateClient();

    [Fact]
    public async Task Upload_is_accepted_and_points_at_the_job()
    {
        using var response = await _client.UploadAsync("notes.md", "# Notes\n\nSome content.");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var job = await response.Content.ReadFromJsonAsync<IngestionJob>(ApiFactory.Json);
        Assert.NotNull(job);
        Assert.Equal("notes.md", job.FileName);
        Assert.EndsWith($"/api/ingestions/{job.Id}", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Background_ingestion_completes_and_the_document_is_listed()
    {
        var job = await _client.IngestAsync("guide.md", "# Guide\n\nFirst section text.\n\n## Part two\n\nSecond section text.");

        Assert.Equal(IngestionStatus.Completed, job.Status);
        Assert.Equal(2, job.ChunkCount);
        Assert.NotNull(job.CompletedAt);

        var documents = await _client.GetFromJsonAsync<List<IndexedDocument>>("/api/documents", ApiFactory.Json);
        var document = Assert.Single(documents!);
        Assert.Equal(job.DocumentId, document.Id);
        Assert.Equal("guide.md", document.Name);

        var jobs = await _client.GetFromJsonAsync<List<IngestionJob>>("/api/ingestions", ApiFactory.Json);
        Assert.Equal(job.Id, Assert.Single(jobs!).Id);
    }

    [Fact]
    public async Task Uploading_identical_content_again_is_recorded_as_a_duplicate()
    {
        var first = await _client.IngestAsync("a.md", "Exactly the same text.");

        var second = await _client.IngestAsync("copy.md", "Exactly the same text.");

        Assert.Equal(IngestionStatus.Duplicate, second.Status);
        Assert.Equal(first.DocumentId, second.DuplicateOfDocumentId);
        Assert.Single((await _client.GetFromJsonAsync<List<IndexedDocument>>("/api/documents", ApiFactory.Json))!);
    }

    [Fact]
    public async Task Content_can_be_uploaded_again_after_its_document_was_deleted()
    {
        var first = await _client.IngestAsync("a.md", "Exactly the same text.");
        (await _client.DeleteAsync($"/api/documents/{first.DocumentId}")).EnsureSuccessStatusCode();

        var second = await _client.IngestAsync("a.md", "Exactly the same text.");

        Assert.Equal(IngestionStatus.Completed, second.Status);
    }

    [Fact]
    public async Task Deleting_a_document_removes_it_from_the_index()
    {
        var job = await _client.IngestAsync("temp.txt", "Temporary text.");

        using var response = await _client.DeleteAsync($"/api/documents/{job.DocumentId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<List<IndexedDocument>>("/api/documents", ApiFactory.Json))!);
    }

    [Theory]
    [InlineData("report.pdf", "%PDF-1.7", "Unsupported file type")]
    [InlineData("empty.txt", "   \n\t ", "contains no text")]
    public async Task Invalid_uploads_are_rejected_with_validation_problems(string fileName, string content, string message)
    {
        using var response = await _client.UploadAsync(fileName, content);

        await AssertValidationProblemAsync(response, "file", message);
    }

    [Fact]
    public async Task Binary_content_is_rejected()
    {
        using var response = await _client.UploadAsync("data.txt", [0x48, 0x69, 0xFF, 0xFE, 0x00]);

        await AssertValidationProblemAsync(response, "file", "not UTF-8 text");
    }

    [Fact]
    public async Task Missing_file_is_rejected()
    {
        using var response = await _client.PostAsync("/api/documents", new MultipartFormDataContent { { new StringContent("x"), "other" } });

        await AssertValidationProblemAsync(response, "file", "field named 'file'");
    }

    [Fact]
    public async Task Unknown_ingestion_job_returns_problem_details()
    {
        using var response = await _client.GetAsync($"/api/ingestions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Ingestion job not found", problem.RootElement.GetProperty("title").GetString());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
    }

    internal static async Task AssertValidationProblemAsync(HttpResponseMessage response, string field, string message)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = problem.RootElement.GetProperty("errors").GetProperty(field);
        Assert.Contains(message, errors[0].GetString());
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
