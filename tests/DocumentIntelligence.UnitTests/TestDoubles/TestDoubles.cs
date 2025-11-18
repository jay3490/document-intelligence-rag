using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DocumentIntelligence.Core.Chat;
using DocumentIntelligence.Core.Chunking;
using DocumentIntelligence.Core.Documents;
using DocumentIntelligence.Core.Embeddings;

namespace DocumentIntelligence.UnitTests.TestDoubles;

/// <summary>Counts one token per whitespace-separated word, which keeps chunking tests readable.</summary>
internal sealed class WordTokenCounter : ITokenCounter
{
    public int CountTokens(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}

/// <summary>Counts one token per character, so a single long word can exceed the budget.</summary>
internal sealed class CharacterTokenCounter : ITokenCounter
{
    public int CountTokens(string text) => text.Length;
}

internal static class Chunks
{
    public static DocumentChunk Create(string text, Guid? documentId = null, int index = 0, string name = "doc.txt", string? section = null)
    {
        var document = documentId ?? Guid.NewGuid();
        return new DocumentChunk(DocumentChunk.CreateId(document, index), document, name, index, section, text, text.Split(' ').Length);
    }
}

/// <summary>Returns a canned reply and records the conversation it was given.</summary>
internal sealed class RecordingChatClient(string reply) : IChatCompletionClient
{
    public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];

    public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        Calls.Add(messages);
        return Task.FromResult(reply);
    }
}

/// <summary>Wraps an embedding client and throws on the configured call number (1-based).</summary>
internal sealed class FailingEmbeddingClient(IEmbeddingClient inner, int failOnCall) : IEmbeddingClient
{
    private int _calls;

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _calls) == failOnCall)
        {
            throw new HttpRequestException("embedding endpoint unavailable");
        }

        return inner.EmbedAsync(texts, cancellationToken);
    }
}

internal sealed record RecordedRequest(HttpMethod Method, string PathAndQuery, string? Body, HttpRequestHeaders Headers);

/// <summary>An HTTP handler that answers from a delegate and keeps every request it saw.</summary>
internal sealed class StubHttpHandler(Func<RecordedRequest, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(request.Method, request.RequestUri!.PathAndQuery, body, request.Headers);
        Requests.Add(recorded);
        return respond(recorded);
    }
}

internal sealed class StubHttpClientFactory(HttpMessageHandler handler, string baseAddress, Action<HttpClient>? configure = null) : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri(baseAddress) };
        configure?.Invoke(client);
        return client;
    }
}
