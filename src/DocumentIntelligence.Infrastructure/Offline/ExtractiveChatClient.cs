using System.Text.RegularExpressions;
using DocumentIntelligence.Core.Answering;
using DocumentIntelligence.Core.Chat;
using DocumentIntelligence.Core.Search;

namespace DocumentIntelligence.Infrastructure.Offline;

/// <summary>
/// A deterministic stand-in for a chat model. It reads the grounded prompt, picks the source
/// sentences that share the most terms with the question and returns them with citations.
/// </summary>
/// <remarks>
/// The output is extractive: every sentence is copied verbatim from a source, so it is always
/// grounded, but it cannot summarise or reason across sources the way a model can.
/// </remarks>
public sealed partial class ExtractiveChatClient : IChatCompletionClient
{
    public const string NotCoveredAnswer = "The provided documents do not cover this question.";

    private const int MaxSentences = 3;

    public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var user = messages.LastOrDefault(m => m.Role == ChatRole.User)
            ?? throw new ArgumentException("The conversation has no user message.", nameof(messages));

        if (!GroundedPrompt.TryParse(user.Content, out var prompt))
        {
            throw new ArgumentException("The offline chat client only answers prompts built by GroundedPrompt.", nameof(messages));
        }

        var questionTerms = TextAnalyzer.Terms(prompt.Question).ToHashSet(StringComparer.Ordinal);

        var picked = prompt.Sources
            .SelectMany(source => SentenceBreak().Split(source.Text)
                .Select(sentence => sentence.Trim())
                .Where(sentence => sentence.Length > 0)
                .Select(sentence => (Sentence: sentence, Source: source.Number)))
            .Select((candidate, order) => (
                candidate.Sentence,
                candidate.Source,
                Order: order,
                Matches: TextAnalyzer.Terms(candidate.Sentence).Distinct(StringComparer.Ordinal).Count(questionTerms.Contains)))
            .Where(candidate => candidate.Matches > 0)
            .DistinctBy(candidate => candidate.Sentence, StringComparer.Ordinal)
            .OrderByDescending(candidate => candidate.Matches)
            .ThenBy(candidate => candidate.Order)
            .Take(MaxSentences)
            .Select(candidate => $"{candidate.Sentence} [{candidate.Source}]")
            .ToList();

        return Task.FromResult(picked.Count == 0 ? NotCoveredAnswer : string.Join(" ", picked));
    }

    [GeneratedRegex(@"(?<=[.!?])\s+|\n+")]
    private static partial Regex SentenceBreak();
}
