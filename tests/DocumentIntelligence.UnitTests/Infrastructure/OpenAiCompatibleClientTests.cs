using System.Net;
using System.Text.Json;
using DocumentIntelligence.Core;
using DocumentIntelligence.Core.Chat;
using DocumentIntelligence.Infrastructure.Ai;
using DocumentIntelligence.UnitTests.TestDoubles;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.UnitTests.Infrastructure;

public sealed class OpenAiCompatibleClientTests
{
    private static readonly AiOptions Options = new()
    {
        Provider = AiProvider.OpenAICompatible,
        BaseUrl = "http://localhost:11434/v1",
        ApiKey = "test-key",
        ChatModel = "llama3.2",
        EmbeddingModel = "nomic-embed-text",
        EmbeddingDimensions = 3,
        Temperature = 0.2,
    };

    [Fact]
    public async Task Embeddings_are_requested_with_the_model_and_returned_in_input_order()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(
            """{"data":[{"index":1,"embedding":[0,1,0]},{"index":0,"embedding":[1,0,0]}]}"""));
        var client = new OpenAiCompatibleEmbeddingClient(Factory(handler), Microsoft.Extensions.Options.Options.Create(Options));

        var vectors = await client.EmbedAsync(["first", "second"], CancellationToken.None);

        Assert.Equal(new[] { 1f, 0f, 0f }, vectors[0]);
        Assert.Equal(new[] { 0f, 1f, 0f }, vectors[1]);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v1/embeddings", request.PathAndQuery);
        Assert.Equal("Bearer test-key", request.Headers.Authorization?.ToString());
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("nomic-embed-text", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(2, body.RootElement.GetProperty("input").GetArrayLength());
    }

    [Fact]
    public async Task Embeddings_of_the_wrong_size_are_rejected_with_a_clear_message()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{"data":[{"index":0,"embedding":[1,0]}]}"""));
        var client = new OpenAiCompatibleEmbeddingClient(Factory(handler), Microsoft.Extensions.Options.Options.Create(Options));

        var error = await Assert.ThrowsAsync<UpstreamServiceException>(() => client.EmbedAsync(["x"], CancellationToken.None));

        Assert.Contains("2-dimensional", error.Message);
        Assert.Equal("embeddings", error.Service);
    }

    [Fact]
    public async Task Chat_sends_roles_model_and_temperature_and_returns_the_message()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(
            """{"choices":[{"index":0,"message":{"role":"assistant","content":"It is 25 days [1]."}}]}"""));
        var client = new OpenAiCompatibleChatClient(Factory(handler), Microsoft.Extensions.Options.Options.Create(Options));

        var answer = await client.CompleteAsync(
            [new ChatMessage(ChatRole.System, "rules"), new ChatMessage(ChatRole.User, "question")],
            CancellationToken.None);

        Assert.Equal("It is 25 days [1].", answer);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v1/chat/completions", request.PathAndQuery);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("llama3.2", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(0.2, body.RootElement.GetProperty("temperature").GetDouble());
        Assert.Equal(
            new[] { "system", "user" },
            body.RootElement.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("role").GetString()));
    }

    [Fact]
    public async Task Error_responses_become_upstream_exceptions_with_the_status()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{"error":"model not found"}""", HttpStatusCode.NotFound));
        var client = new OpenAiCompatibleChatClient(Factory(handler), Microsoft.Extensions.Options.Options.Create(Options));

        var error = await Assert.ThrowsAsync<UpstreamServiceException>(() =>
            client.CompleteAsync([new ChatMessage(ChatRole.User, "q")], CancellationToken.None));

        Assert.Equal("chat", error.Service);
        Assert.Contains("404", error.Message);
        Assert.Contains("model not found", error.Message);
    }

    [Fact]
    public async Task Connection_failures_become_upstream_exceptions()
    {
        var handler = new StubHttpHandler(_ => throw new HttpRequestException("connection refused"));
        var client = new OpenAiCompatibleEmbeddingClient(Factory(handler), Microsoft.Extensions.Options.Options.Create(Options));

        var error = await Assert.ThrowsAsync<UpstreamServiceException>(() => client.EmbedAsync(["x"], CancellationToken.None));

        Assert.Contains("connection refused", error.Message);
    }

    private static StubHttpClientFactory Factory(StubHttpHandler handler) =>
        new(handler, "http://unused/", client => OpenAiHttpClient.Configure(client, Options));
}
