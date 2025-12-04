using System.Globalization;
using System.Text.RegularExpressions;
using DocumentIntelligence.Core.Documents;
using DocumentIntelligence.Core.Search;

namespace DocumentIntelligence.Api.Endpoints;

/// <summary>Optional metadata filters accepted by the query endpoint.</summary>
public sealed record QueryFilters(IReadOnlyList<string>? Tags, string? Source, DateTimeOffset? From, DateTimeOffset? To);

/// <summary>Validates document metadata and filters supplied by clients.</summary>
public static partial class MetadataInput
{
    /// <summary>Parses the optional <c>tags</c>, <c>source</c> and <c>date</c> upload form fields.</summary>
    public static DocumentMetadata ParseUploadFields(IFormCollection form, DateTimeOffset uploadedAt, Dictionary<string, string[]> errors)
    {
        var tags = SplitTags(form["tags"]);
        ValidateTags(tags, "tags", errors);

        var source = NullIfBlank(form["source"].ToString());
        ValidateSource(source, "source", errors);

        DateTimeOffset? date = uploadedAt;
        var rawDate = NullIfBlank(form["date"].ToString());
        if (rawDate is not null)
        {
            if (DateTimeOffset.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                date = parsed.ToUniversalTime();
            }
            else
            {
                errors["date"] = ["The date must be an ISO 8601 date or date and time."];
            }
        }

        return new DocumentMetadata { Source = source, Tags = tags, Date = date };
    }

    /// <summary>Builds the search filter for a tenant from optional request filters.</summary>
    public static SearchFilter ToSearchFilter(string tenantId, QueryFilters? filters, Dictionary<string, string[]> errors)
    {
        if (filters is null)
        {
            return new SearchFilter(tenantId);
        }

        var tags = filters.Tags ?? [];
        ValidateTags(tags, "filters.tags", errors);

        var source = NullIfBlank(filters.Source);
        ValidateSource(source, "filters.source", errors);

        if (filters.From is { } from && filters.To is { } to && from > to)
        {
            errors["filters.from"] = ["filters.from must not be later than filters.to."];
        }

        return new SearchFilter(tenantId)
        {
            Tags = tags,
            Source = source,
            From = filters.From?.ToUniversalTime(),
            To = filters.To?.ToUniversalTime(),
        };
    }

    private static List<string> SplitTags(IEnumerable<string?> values) =>
        values.SelectMany(value => (value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .ToList();

    private static void ValidateTags(IReadOnlyCollection<string> tags, string field, Dictionary<string, string[]> errors)
    {
        if (tags.Count > DocumentMetadata.MaxTags)
        {
            errors[field] = [$"At most {DocumentMetadata.MaxTags} tags are allowed."];
        }
        else if (tags.Any(tag => !TagPattern().IsMatch(tag.Trim())))
        {
            errors[field] = [$"Tags must be 1 to {DocumentMetadata.MaxTagLength} letters, digits, '-', '_', '.' or ':'."];
        }
    }

    private static void ValidateSource(string? source, string field, Dictionary<string, string[]> errors)
    {
        if (source is { Length: > DocumentMetadata.MaxSourceLength })
        {
            errors[field] = [$"The source must be at most {DocumentMetadata.MaxSourceLength} characters."];
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"^[\p{L}\p{N}_.:-]{1,64}$")]
    private static partial Regex TagPattern();
}
