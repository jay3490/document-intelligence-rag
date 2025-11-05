using System.Globalization;
using System.Text.RegularExpressions;

namespace DocumentIntelligence.Core.Answering;

/// <summary>Finds the source numbers an answer cites, such as [1], [2][3] or [1, 4].</summary>
public static partial class CitationParser
{
    /// <returns>
    /// Distinct source numbers in order of first appearance. Numbers outside
    /// <c>1..sourceCount</c> are ignored, since they cannot refer to a supplied source.
    /// </returns>
    public static IReadOnlyList<int> Parse(string answer, int sourceCount)
    {
        ArgumentNullException.ThrowIfNull(answer);

        var cited = new List<int>();
        foreach (Match match in CitationPattern().Matches(answer))
        {
            foreach (var part in match.Groups["numbers"].Value.Split(','))
            {
                var number = int.Parse(part.Trim(), CultureInfo.InvariantCulture);
                if (number >= 1 && number <= sourceCount && !cited.Contains(number))
                {
                    cited.Add(number);
                }
            }
        }

        return cited;
    }

    [GeneratedRegex(@"\[(?<numbers>\d{1,4}(?:\s*,\s*\d{1,4})*)\]")]
    private static partial Regex CitationPattern();
}
