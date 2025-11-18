using DocumentIntelligence.Core.Answering;

namespace DocumentIntelligence.UnitTests.Answering;

public sealed class CitationParserTests
{
    [Theory]
    [InlineData("Leave is 25 days [1].", 3, new[] { 1 })]
    [InlineData("A [2][3] and B [1].", 3, new[] { 2, 3, 1 })]
    [InlineData("Combined [1, 3] then [3] again.", 3, new[] { 1, 3 })]
    [InlineData("Out of range [0] and [7] and [2].", 3, new[] { 2 })]
    [InlineData("No citations here.", 3, new int[0])]
    [InlineData("Markdown links [text](url) and [a] are ignored [1].", 2, new[] { 1 })]
    public void Extracts_distinct_valid_source_numbers_in_order(string answer, int sourceCount, int[] expected)
    {
        Assert.Equal(expected, CitationParser.Parse(answer, sourceCount));
    }
}
