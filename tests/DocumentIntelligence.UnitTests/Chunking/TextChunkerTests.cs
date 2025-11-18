using System.ComponentModel.DataAnnotations;
using DocumentIntelligence.Core.Chunking;
using DocumentIntelligence.Core.Documents;
using DocumentIntelligence.Infrastructure.Tokenization;
using DocumentIntelligence.UnitTests.TestDoubles;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.UnitTests.Chunking;

public sealed class TextChunkerTests
{
    private static TextChunker CreateChunker(int maxTokens, int overlapTokens, ITokenCounter? counter = null) =>
        new(counter ?? new WordTokenCounter(), Options.Create(new ChunkingOptions { MaxTokens = maxTokens, OverlapTokens = overlapTokens }));

    [Fact]
    public void Short_text_becomes_a_single_chunk()
    {
        var chunks = CreateChunker(50, 5).Chunk("One sentence. Another sentence.", DocumentFormat.PlainText);

        var chunk = Assert.Single(chunks);
        Assert.Equal("One sentence. Another sentence.", chunk.Text);
        Assert.Equal(0, chunk.Index);
        Assert.Null(chunk.Section);
        Assert.Equal(4, chunk.TokenCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\n  \t ")]
    public void Blank_text_produces_no_chunks(string content)
    {
        Assert.Empty(CreateChunker(50, 5).Chunk(content, DocumentFormat.PlainText));
    }

    [Fact]
    public void Chunks_never_exceed_the_token_budget()
    {
        var text = string.Join(" ", Enumerable.Range(1, 60).Select(i => $"Sentence number {i} has six words."));

        var chunks = CreateChunker(maxTokens: 20, overlapTokens: 6).Chunk(text, DocumentFormat.PlainText);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.InRange(chunk.TokenCount, 1, 20));
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.Index));
    }

    [Fact]
    public void Consecutive_chunks_overlap_by_whole_trailing_sentences()
    {
        var text = "Alpha one two three. Bravo one two three. Charlie one two three. Delta one two three. Echo one two three.";

        var chunks = CreateChunker(maxTokens: 9, overlapTokens: 4).Chunk(text, DocumentFormat.PlainText);

        Assert.Equal(
            ["Alpha one two three. Bravo one two three.",
             "Bravo one two three. Charlie one two three.",
             "Charlie one two three. Delta one two three.",
             "Delta one two three. Echo one two three."],
            chunks.Select(c => c.Text));
    }

    [Fact]
    public void Zero_overlap_partitions_the_text()
    {
        var text = "Alpha one two three. Bravo one two three. Charlie one two three.";

        var chunks = CreateChunker(maxTokens: 9, overlapTokens: 0).Chunk(text, DocumentFormat.PlainText);

        Assert.Equal(
            ["Alpha one two three. Bravo one two three.", "Charlie one two three."],
            chunks.Select(c => c.Text));
    }

    [Fact]
    public void Paragraph_and_line_breaks_are_preserved_inside_a_chunk()
    {
        var chunks = CreateChunker(50, 0).Chunk("First paragraph.\n\nSecond paragraph\nwith a second line.", DocumentFormat.PlainText);

        Assert.Equal("First paragraph.\n\nSecond paragraph\nwith a second line.", Assert.Single(chunks).Text);
    }

    [Fact]
    public void Markdown_headings_split_sections_and_record_the_heading_path()
    {
        const string markdown = """
            # Handbook

            Welcome to the handbook.

            ## Leave

            You get 25 days.

            ### Carry over

            Unused days expire in March.

            ## Expenses

            File within 30 days.
            """;

        var chunks = CreateChunker(100, 10).Chunk(markdown, DocumentFormat.Markdown);

        Assert.Equal(
            [("Handbook", "Welcome to the handbook."),
             ("Handbook › Leave", "You get 25 days."),
             ("Handbook › Leave › Carry over", "Unused days expire in March."),
             ("Handbook › Expenses", "File within 30 days.")],
            chunks.Select(c => (c.Section, c.Text)));
    }

    [Fact]
    public void Hash_lines_inside_code_fences_are_not_headings()
    {
        const string markdown = """
            # Setup

            ```bash
            # install the tools
            make install
            ```
            """;

        var chunk = Assert.Single(CreateChunker(100, 0).Chunk(markdown, DocumentFormat.Markdown));

        Assert.Equal("Setup", chunk.Section);
        Assert.Contains("# install the tools", chunk.Text);
    }

    [Fact]
    public void Plain_text_ignores_markdown_syntax()
    {
        var chunk = Assert.Single(CreateChunker(100, 0).Chunk("# not a heading\n\nbody", DocumentFormat.PlainText));

        Assert.Null(chunk.Section);
        Assert.StartsWith("# not a heading", chunk.Text);
    }

    [Fact]
    public void A_sentence_longer_than_the_budget_is_split_on_words()
    {
        var sentence = string.Join(" ", Enumerable.Range(1, 25).Select(i => $"w{i}"));

        var chunks = CreateChunker(maxTokens: 10, overlapTokens: 0).Chunk(sentence, DocumentFormat.PlainText);

        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, chunk => Assert.True(chunk.TokenCount <= 10));
        Assert.Equal(sentence, string.Join(" ", chunks.Select(c => c.Text)));
    }

    [Fact]
    public void A_word_longer_than_the_budget_is_sliced()
    {
        var word = new string('x', 45);

        var chunks = CreateChunker(maxTokens: 16, overlapTokens: 0, new CharacterTokenCounter()).Chunk(word, DocumentFormat.PlainText);

        Assert.All(chunks, chunk => Assert.True(chunk.TokenCount <= 16));
        Assert.Equal(word, string.Concat(chunks.Select(c => c.Text)));
    }

    [Fact]
    public void Real_token_counts_stay_within_the_budget()
    {
        var paragraph = string.Join(" ", Enumerable.Range(1, 40).Select(i =>
            $"Clause {i}: the contractor shall deliver item #{i * 7} by 2026-0{i % 9 + 1}-1{i % 9}, unless agreed otherwise."));
        var text = string.Join("\n\n", Enumerable.Repeat(paragraph, 5));

        var chunks = CreateChunker(maxTokens: 128, overlapTokens: 24, new TiktokenTokenCounter()).Chunk(text, DocumentFormat.PlainText);

        Assert.True(chunks.Count > 5);
        Assert.All(chunks, chunk => Assert.InRange(chunk.TokenCount, 1, 128));
    }

    [Fact]
    public void Options_reject_an_overlap_that_is_not_smaller_than_the_chunk()
    {
        var options = new ChunkingOptions { MaxTokens = 64, OverlapTokens = 64 };
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        Assert.False(valid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(ChunkingOptions.OverlapTokens)));
    }
}
