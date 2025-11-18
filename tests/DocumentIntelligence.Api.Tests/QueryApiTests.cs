using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentIntelligence.Core;
using DocumentIntelligence.Core.Answering;
using DocumentIntelligence.Core.Chat;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentIntelligence.Api.Tests;

public sealed class QueryApiTests : IDisposable
{
    private const string Handbook = """
        # Employee handbook

        ## Vacation

        Full-time employees receive 25 days of paid vacation per year. Unused days carry over until March 31.

        ## Expenses

        Expense reports must be filed within 30 days through the finance portal. Receipts are required above 50 EUR.
        """;

    private readonly ApiFactory _factory = new();

    [Fact]
    public async Task Answers_with_citations_from_ingested_documents()
    {
        using var client = _factory.CreateClient();
        var job = await client.IngestAsync("handbook.md", Handbook);

        using var response = await client.PostAsJsonAsync("/api/query", new { question = "How many vacation days do employees get?" });

        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<AnswerResult>(ApiFactory.Json))!;
        Assert.Contains("25 days of paid vacation", result.Answer);
        var citation = Assert.Single(result.Citations);
        Assert.Equal(job.DocumentId, citation.DocumentId);
        Assert.Equal("handbook.md", citation.DocumentName);
        Assert.Equal("Employee handbook › Vacation", citation.Section);
        Assert.Contains($"[{citation.Number}]", result.Answer);
    }

    [Fact]
    public async Task Says_so_when_nothing_has_been_ingested()
    {
        using var client = _factory.CreateClient();

        var result = await (await client.PostAsJsonAsync("/api/query", new { question = "Anything?" }))
            .Content.ReadFromJsonAsync<AnswerResult>(ApiFactory.Json);

        Assert.Equal(QuestionAnsweringService.NothingIndexedAnswer, result!.Answer);
        Assert.Empty(result.Citations);
    }

    [Theory]
    [InlineData("""{"question":""}""", "question")]
    [InlineData("""{"question":"ok","topK":0}""", "topK")]
    [InlineData("""{"question":"ok","topK":21}""", "topK")]
    public async Task Invalid_queries_are_rejected(string body, string field)
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsync("/api/query", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Overlong_questions_are_rejected()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/query", new { question = new string('a', 2001) });

        await DocumentApiTests.AssertValidationProblemAsync(response, "question", "at most 2000 characters");
    }

    [Fact]
    public async Task A_failing_model_endpoint_becomes_a_502_problem()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IChatCompletionClient, UnavailableChatClient>()));
        using var client = factory.CreateClient();
        await client.IngestAsync("handbook.md", Handbook);

        using var response = await client.PostAsJsonAsync("/api/query", new { question = "vacation days?" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("chat", problem.RootElement.GetProperty("service").GetString());
        Assert.Equal("A dependency failed", problem.RootElement.GetProperty("title").GetString());
    }

    public void Dispose() => _factory.Dispose();

    private sealed class UnavailableChatClient : IChatCompletionClient
    {
        public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken) =>
            throw new UpstreamServiceException("chat", "chat returned 503 Service Unavailable");
    }
}
