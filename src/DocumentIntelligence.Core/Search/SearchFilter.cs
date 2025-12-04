using DocumentIntelligence.Core.Documents;

namespace DocumentIntelligence.Core.Search;

/// <summary>
/// Narrows retrieval to one tenant and, optionally, to chunks whose document metadata matches.
/// All supplied conditions must hold; a chunk matches the tag condition when it carries at
/// least one of the requested tags. Date bounds are inclusive.
/// </summary>
public sealed class SearchFilter
{
    public SearchFilter(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        TenantId = tenantId;
    }

    private readonly IReadOnlyList<string> _tags = [];

    public string TenantId { get; }

    /// <summary>Requested tags, normalised the same way as <see cref="DocumentMetadata.Tags"/>.</summary>
    public IReadOnlyList<string> Tags
    {
        get => _tags;
        init => _tags = DocumentMetadata.NormalizeTags(value);
    }

    public string? Source { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    /// <summary>True when the filter has conditions beyond the tenant.</summary>
    public bool HasMetadataConditions => Tags.Count > 0 || Source is not null || From is not null || To is not null;

    public bool Matches(DocumentChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        if (!string.Equals(chunk.TenantId, TenantId, StringComparison.Ordinal))
        {
            return false;
        }

        var metadata = chunk.Metadata;
        if (Tags.Count > 0 && !metadata.Tags.Any(tag => Tags.Contains(tag, StringComparer.Ordinal)))
        {
            return false;
        }

        if (Source is not null && !string.Equals(metadata.Source, Source, StringComparison.Ordinal))
        {
            return false;
        }

        if (From is not null && (metadata.Date is null || metadata.Date < From))
        {
            return false;
        }

        return To is null || (metadata.Date is not null && metadata.Date <= To);
    }

    /// <summary>A stable text form of the filter, used to build cache keys.</summary>
    public string ToCanonicalString() =>
        string.Join(
            '|',
            $"tenant={TenantId}",
            $"tags={string.Join(',', Tags)}",
            $"source={Source}",
            $"from={From?.UtcDateTime:O}",
            $"to={To?.UtcDateTime:O}");
}
