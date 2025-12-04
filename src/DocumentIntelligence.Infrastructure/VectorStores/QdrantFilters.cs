using DocumentIntelligence.Core.Search;

namespace DocumentIntelligence.Infrastructure.VectorStores;

/// <summary>Builds Qdrant payload filters and the payload indexes they rely on.</summary>
internal static class QdrantFilters
{
    /// <summary>The payload indexes every collection gets; the filters below only use indexed keys.</summary>
    public static readonly IReadOnlyList<object> PayloadIndexes =
    [
        new { field_name = "document_id", field_schema = "keyword" },
        new { field_name = "tenant_id", field_schema = new { type = "keyword", is_tenant = true } },
        new { field_name = "tags", field_schema = "keyword" },
        new { field_name = "source", field_schema = "keyword" },
        new { field_name = "date", field_schema = "datetime" },
    ];

    public static object ForDocument(Guid documentId) =>
        new { must = new[] { new { key = "document_id", match = new { value = documentId.ToString() } } } };

    /// <summary>
    /// The tenant condition is always present. Tags match when a point carries any of them;
    /// the date range is inclusive at both ends.
    /// </summary>
    public static object Build(SearchFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var must = new List<object> { new { key = "tenant_id", match = new { value = filter.TenantId } } };

        if (filter.Tags.Count > 0)
        {
            must.Add(new { key = "tags", match = new { any = filter.Tags } });
        }

        if (filter.Source is not null)
        {
            must.Add(new { key = "source", match = new { value = filter.Source } });
        }

        if (filter.From is not null || filter.To is not null)
        {
            must.Add(new { key = "date", range = new DateRange(filter.From?.ToUniversalTime(), filter.To?.ToUniversalTime()) });
        }

        return new { must };
    }

    private sealed record DateRange(DateTimeOffset? Gte, DateTimeOffset? Lte);
}
