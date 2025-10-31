using System.Text.RegularExpressions;

namespace DocumentIntelligence.Core.Search;

/// <summary>
/// Turns text into lower-cased search terms: runs of letters and digits, minus a short list of
/// English stop words. Used by keyword scoring so that a query and a document agree on terms.
/// </summary>
public static partial class TextAnalyzer
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "as", "at", "be", "been", "but", "by", "can", "do", "does",
        "for", "from", "had", "has", "have", "how", "i", "if", "in", "into", "is", "it", "its",
        "of", "on", "or", "our", "so", "than", "that", "the", "their", "then", "there", "these",
        "they", "this", "to", "was", "we", "were", "what", "when", "where", "which", "who",
        "why", "will", "with", "you", "your",
    };

    public static IEnumerable<string> Terms(string text)
    {
        foreach (Match match in WordPattern().Matches(text))
        {
            var term = match.Value.ToLowerInvariant();
            if (!StopWords.Contains(term))
            {
                yield return term;
            }
        }
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordPattern();
}
