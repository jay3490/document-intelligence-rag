using System.Text;
using System.Text.RegularExpressions;
using DocumentIntelligence.Core.Documents;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Core.Chunking;

/// <summary>
/// Splits documents into overlapping chunks whose size is bounded in model tokens.
/// </summary>
/// <remarks>
/// Text is broken into sentences, falling back to words and then to character slices for
/// anything too long to fit in a chunk on its own. Sentences are packed greedily up to
/// <see cref="ChunkingOptions.MaxTokens"/>, and every chunk after the first in a section
/// starts with the trailing sentences of its predecessor, up to
/// <see cref="ChunkingOptions.OverlapTokens"/>. Markdown headings are hard boundaries: a
/// chunk never spans two sections, and each chunk records the heading path it sits under.
/// </remarks>
public sealed partial class TextChunker
{
    /// <summary>Joins the levels of a Markdown heading path, for example "Setup › Linux".</summary>
    public const string SectionSeparator = " › ";

    private readonly ITokenCounter _tokenCounter;
    private readonly int _maxTokens;
    private readonly int _overlapTokens;

    public TextChunker(ITokenCounter tokenCounter, IOptions<ChunkingOptions> options)
    {
        _tokenCounter = tokenCounter;
        _maxTokens = options.Value.MaxTokens;
        _overlapTokens = options.Value.OverlapTokens;
    }

    public IReadOnlyList<TextChunk> Chunk(string content, DocumentFormat format)
    {
        ArgumentNullException.ThrowIfNull(content);

        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        var sections = format == DocumentFormat.Markdown
            ? SplitMarkdownSections(normalized)
            : [new Section(null, normalized)];

        var chunks = new List<TextChunk>();
        foreach (var section in sections)
        {
            foreach (var window in Pack(SplitIntoSegments(section.Body)))
            {
                var text = Join(window);
                chunks.Add(new TextChunk(chunks.Count, section.Title, text, _tokenCounter.CountTokens(text)));
            }
        }

        return chunks;
    }

    private static List<Section> SplitMarkdownSections(string text)
    {
        var sections = new List<Section>();
        var headingPath = new List<(int Level, string Title)>();
        var body = new StringBuilder();
        string? title = null;
        var inFence = false;

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
            }
            else if (!inFence && HeadingPattern().Match(line) is { Success: true } heading)
            {
                AddSection(sections, title, body);

                var level = heading.Groups["level"].Length;
                headingPath.RemoveAll(h => h.Level >= level);
                headingPath.Add((level, heading.Groups["title"].Value));
                title = string.Join(SectionSeparator, headingPath.Select(h => h.Title));
                continue;
            }

            body.Append(line).Append('\n');
        }

        AddSection(sections, title, body);
        return sections;

        static void AddSection(List<Section> sections, string? title, StringBuilder body)
        {
            var content = body.ToString();
            if (!string.IsNullOrWhiteSpace(content))
            {
                sections.Add(new Section(title, content));
            }

            body.Clear();
        }
    }

    /// <summary>Breaks a section body into segments that each fit within the token budget.</summary>
    private List<Segment> SplitIntoSegments(string body)
    {
        var segments = new List<Segment>();

        foreach (var paragraph in ParagraphBreak().Split(body))
        {
            var separator = Separator.Paragraph;
            foreach (var rawLine in paragraph.Split('\n'))
            {
                var line = rawLine.TrimEnd();
                if (line.Length == 0)
                {
                    continue;
                }

                foreach (var sentence in SentenceBreak().Split(line))
                {
                    foreach (var piece in FitToBudget(sentence))
                    {
                        segments.Add(new Segment(piece, _tokenCounter.CountTokens(piece), separator));
                        separator = Separator.Space;
                    }
                }

                separator = Separator.Line;
            }
        }

        return segments;
    }

    private IEnumerable<string> FitToBudget(string sentence)
    {
        if (_tokenCounter.CountTokens(sentence) <= _maxTokens)
        {
            yield return sentence;
            yield break;
        }

        // Pieces are measured exactly. The buffer never grows past the budget, so each count
        // stays cheap even though it is repeated for every word.
        var current = string.Empty;

        foreach (var word in Whitespace().Split(sentence.Trim()))
        {
            if (_tokenCounter.CountTokens(word) > _maxTokens)
            {
                if (current.Length > 0)
                {
                    yield return current;
                    current = string.Empty;
                }

                foreach (var slice in SliceOversizedWord(word))
                {
                    yield return slice;
                }

                continue;
            }

            var candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && _tokenCounter.CountTokens(candidate) > _maxTokens)
            {
                yield return current;
                candidate = word;
            }

            current = candidate;
        }

        if (current.Length > 0)
        {
            yield return current;
        }
    }

    /// <summary>Cuts a single token-heavy word (a URL, a base64 blob) into slices that fit.</summary>
    private IEnumerable<string> SliceOversizedWord(string word)
    {
        var start = 0;
        while (start < word.Length)
        {
            var length = Math.Min(word.Length - start, _maxTokens * 2);
            while (length > 1 && _tokenCounter.CountTokens(word.Substring(start, length)) > _maxTokens)
            {
                length = Math.Max(1, length * 3 / 4);
            }

            if (length > 1 && start + length < word.Length && char.IsHighSurrogate(word[start + length - 1]))
            {
                length--;
            }

            yield return word.Substring(start, length);
            start += length;
        }
    }

    /// <summary>Greedily packs segments into windows, carrying an overlap tail between them.</summary>
    private IEnumerable<IReadOnlyList<Segment>> Pack(IReadOnlyList<Segment> segments)
    {
        var window = new List<Segment>();

        foreach (var segment in segments)
        {
            if (window.Count > 0 && Cost(window) + 1 + segment.Tokens > _maxTokens)
            {
                yield return window.ToArray();

                window = OverlapTail(window);
                while (window.Count > 0 && Cost(window) + 1 + segment.Tokens > _maxTokens)
                {
                    window.RemoveAt(0);
                }
            }

            window.Add(segment);
        }

        // Every emitted window is immediately followed by a new segment, so a non-empty
        // window here always holds text that has not been emitted yet.
        if (window.Count > 0)
        {
            yield return window;
        }
    }

    private List<Segment> OverlapTail(List<Segment> window)
    {
        var start = window.Count;
        var tokens = 0;
        while (start > 0)
        {
            var next = tokens + window[start - 1].Tokens + (start == window.Count ? 0 : 1);
            if (next > _overlapTokens)
            {
                break;
            }

            tokens = next;
            start--;
        }

        return window.GetRange(start, window.Count - start);
    }

    /// <summary>Estimated tokens of a joined window, counting one token per separator.</summary>
    private static int Cost(List<Segment> window) => window.Sum(s => s.Tokens) + window.Count - 1;

    private static string Join(IReadOnlyList<Segment> window)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < window.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(window[i].Separator switch
                {
                    Separator.Paragraph => "\n\n",
                    Separator.Line => "\n",
                    _ => " ",
                });
            }

            builder.Append(window[i].Text);
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"^ {0,3}(?<level>#{1,6})[ \t]+(?<title>.+?)(?:[ \t]+#+)?[ \t]*$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"\n[ \t]*\n")]
    private static partial Regex ParagraphBreak();

    [GeneratedRegex(@"(?<=[.!?])\s+(?=\S)")]
    private static partial Regex SentenceBreak();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private sealed record Section(string? Title, string Body);

    private enum Separator
    {
        Space,
        Line,
        Paragraph,
    }

    private readonly record struct Segment(string Text, int Tokens, Separator Separator);
}
