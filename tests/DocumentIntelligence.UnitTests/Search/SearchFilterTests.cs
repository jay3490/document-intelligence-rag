using DocumentIntelligence.Core.Documents;
using DocumentIntelligence.Core.Search;
using DocumentIntelligence.Core.Tenancy;
using DocumentIntelligence.UnitTests.TestDoubles;

namespace DocumentIntelligence.UnitTests.Search;

public sealed class SearchFilterTests
{
    private static readonly DateTimeOffset March = new(2024, 3, 15, 0, 0, 0, TimeSpan.Zero);

    private static readonly DocumentChunk Tagged = Chunks.Create("text") with
    {
        TenantId = "acme",
        Metadata = new DocumentMetadata { Source = "wiki", Tags = ["hr", "policy"], Date = March },
    };

    [Fact]
    public void A_tenant_only_filter_matches_every_chunk_of_that_tenant()
    {
        Assert.True(new SearchFilter("acme").Matches(Tagged));
        Assert.False(new SearchFilter("globex").Matches(Tagged));
    }

    [Theory]
    [InlineData(new[] { "hr" }, true)]
    [InlineData(new[] { "finance", "POLICY" }, true)]
    [InlineData(new[] { "finance" }, false)]
    public void Tags_match_when_any_requested_tag_is_present(string[] tags, bool expected) =>
        Assert.Equal(expected, new SearchFilter("acme") { Tags = tags }.Matches(Tagged));

    [Fact]
    public void Source_must_match_exactly()
    {
        Assert.True(new SearchFilter("acme") { Source = "wiki" }.Matches(Tagged));
        Assert.False(new SearchFilter("acme") { Source = "portal" }.Matches(Tagged));
    }

    [Fact]
    public void Date_bounds_are_inclusive()
    {
        Assert.True(new SearchFilter("acme") { From = March, To = March }.Matches(Tagged));
        Assert.False(new SearchFilter("acme") { From = March.AddDays(1) }.Matches(Tagged));
        Assert.False(new SearchFilter("acme") { To = March.AddDays(-1) }.Matches(Tagged));
    }

    [Fact]
    public void Chunks_without_a_date_never_match_a_date_range()
    {
        var undated = Chunks.Create("text");

        Assert.False(new SearchFilter(TenantId.Default) { From = DateTimeOffset.UnixEpoch }.Matches(undated));
    }

    [Fact]
    public void The_canonical_form_ignores_tag_order_and_case()
    {
        var first = new SearchFilter("acme") { Tags = ["B", "a"], Source = "wiki" };
        var second = new SearchFilter("acme") { Tags = ["a", "b", "a"], Source = "wiki" };

        Assert.Equal(first.ToCanonicalString(), second.ToCanonicalString());
        Assert.NotEqual(first.ToCanonicalString(), new SearchFilter("globex") { Tags = ["a", "b"], Source = "wiki" }.ToCanonicalString());
    }

    [Fact]
    public void Metadata_normalises_tags_and_compares_by_value()
    {
        var metadata = new DocumentMetadata { Tags = [" Policy ", "hr", "HR", ""] };

        Assert.Equal(new[] { "hr", "policy" }, metadata.Tags);
        Assert.Equal(metadata, new DocumentMetadata { Tags = ["policy", "hr"] });
        Assert.Equal(metadata.GetHashCode(), new DocumentMetadata { Tags = ["policy", "hr"] }.GetHashCode());
    }

    [Theory]
    [InlineData("acme", true)]
    [InlineData("tenant_01-a", true)]
    [InlineData("Acme", false)]
    [InlineData("-acme", false)]
    [InlineData("", false)]
    public void Tenant_ids_are_validated(string value, bool expected) => Assert.Equal(expected, TenantId.IsValid(value));

    [Fact]
    public void The_keyword_index_applies_the_filter_before_ranking()
    {
        var index = new Bm25Index();
        var other = Chunks.Create("policy text", index: 1) with { TenantId = "globex" };
        var mine = Chunks.Create("policy text", index: 2) with { TenantId = "acme", Metadata = new DocumentMetadata { Tags = ["hr"] } };
        index.Add([other, mine]);

        var hit = Assert.Single(index.Search("policy", 10, new SearchFilter("acme") { Tags = ["hr"] }));

        Assert.Equal(mine.Id, hit.Chunk.Id);
        Assert.Equal(mine.DocumentId, Assert.Single(index.ListDocuments("acme")).Id);
        Assert.Equal("globex", index.FindDocumentTenant(other.DocumentId));
        Assert.Null(index.FindDocumentTenant(Guid.NewGuid()));
    }
}
