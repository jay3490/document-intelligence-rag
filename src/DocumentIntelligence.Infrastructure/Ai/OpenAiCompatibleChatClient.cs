using DocumentIntelligence.Core;
using DocumentIntelligence.Core.Chat;
using DocumentIntelligence.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Infrastructure.Ai;

/// <summary>Calls <c>POST {BaseUrl}/chat/completions</c> on an OpenAI-compatible server.</summary>
public sealed class OpenAiCompatibleChatClient(IHttpClientFactory httpClientFactory, IOptions<AiOptions> options) : IChatCompletionClient
{
    private const string Service = "chat";

    public async Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var ai = options.Value;
        var request = new ChatRequest(
            ai.ChatModel,
            messages.Select(m => new WireMessage(RoleName(m.Role), m.Content)).ToList(),
            ai.Temperature);

        var client = httpClientFactory.CreateClient(OpenAiHttpClient.Name);
        var response = await JsonHttp.SendAsync<ChatResponse>(
            client, HttpMethod.Post, "chat/completions", request, Service, cancellationToken);

        return response.Choices.FirstOrDefault()?.Message?.Content
            ?? throw new UpstreamServiceException(Service, $"Model '{ai.ChatModel}' returned no message content.");
    }

    private static string RoleName(ChatRole role) => role switch
    {
        ChatRole.System => "system",
        ChatRole.User => "user",
        ChatRole.Assistant => "assistant",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    private sealed record ChatRequest(string Model, IReadOnlyList<WireMessage> Messages, double Temperature);

    private sealed record WireMessage(string Role, string? Content);

    private sealed record ChatResponse(IReadOnlyList<Choice> Choices);

    private sealed record Choice(WireMessage? Message);
}
