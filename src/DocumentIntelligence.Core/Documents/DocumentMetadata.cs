namespace DocumentIntelligence.Core.Documents;

/// <summary>
/// Descriptive fields supplied with a document and copied onto each of its chunks, so that
/// retrieval can be narrowed by them. <see cref="Tags"/> are normalised: trimmed, lower-case,
/// distinct and sorted.
/// </summary>
public sealed record DocumentMetadata
{
    public const int MaxTags = 20;
    public const int MaxTagLength = 64;
    public const int MaxSourceLength = 200;

    public static readonly DocumentMetadata None = new();

    private readonly IReadOnlyList<string> _tags = [];

    /// <summary>Where the document came from, for example "hr-portal" or "wiki".</summary>
    public string? Source { get; init; }

    public IReadOnlyList<string> Tags
    {
        get => _tags;
        init => _tags = NormalizeTags(value);
    }

    /// <summary>The date the document is about or was published; defaults to the upload time.</summary>
    public DateTimeOffset? Date { get; init; }

    public static IReadOnlyList<string> NormalizeTags(IEnumerable<string>? tags) =>
        tags is null
            ? []
            : tags.Select(tag => tag.Trim().ToLowerInvariant())
                .Where(tag => tag.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();

    public bool Equals(DocumentMetadata? other) =>
        other is not null &&
        Source == other.Source &&
        Date == other.Date &&
        Tags.SequenceEqual(other.Tags, StringComparer.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Source);
        hash.Add(Date);
        foreach (var tag in Tags)
        {
            hash.Add(tag, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
