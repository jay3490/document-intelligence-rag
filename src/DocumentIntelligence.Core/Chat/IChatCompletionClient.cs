namespace DocumentIntelligence.Core.Chat;

/// <summary>Produces the next assistant message for a conversation.</summary>
public interface IChatCompletionClient
{
    Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken);
}
