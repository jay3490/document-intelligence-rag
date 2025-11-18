using DocumentIntelligence.Core.Search;
using DocumentIntelligence.UnitTests.TestDoubles;

namespace DocumentIntelligence.UnitTests.Search;

public sealed class Bm25IndexTests
{
    [Fact]
    public void Scores_match_the_bm25_formula()
    {
        var index = new Bm25Index(k1: 1.2, b: 0.75);
        var target = Chunks.Create("apple cherry cherry");
        index.Add([Chunks.Create("apple banana"), target, Chunks.Create("date")]);

        var hit = Assert.Single(index.Search("cherry", 10));

        // N = 3, df = 1, tf = 2, |d| = 3, avgdl = 2
        var idf = Math.Log(1 + ((3 - 1 + 0.5) / (1 + 0.5)));
        var expected = idf * 2 * (1.2 + 1) / (2 + (1.2 * (1 - 0.75 + (0.75 * 3 / 2.0))));
        Assert.Equal(target.Id, hit.Chunk.Id);
        Assert.Equal(expected, hit.Score, precision: 10);
    }

    [Fact]
    public void Rare_terms_outweigh_common_terms()
    {
        var index = new Bm25Index();
        var rare = Chunks.Create("invoice reference INV-2291 was rejected");
        index.Add(
        [
            Chunks.Create("invoice paid"),
            Chunks.Create("invoice approved"),
            Chunks.Create("invoice archived"),
            rare,
        ]);

        var hits = index.Search("invoice 2291", 10);

        Assert.Equal(rare.Id, hits[0].Chunk.Id);
        Assert.Equal(4, hits.Count);
    }

    [Fact]
    public void Shorter_chunks_win_when_term_frequency_is_equal()
    {
        var index = new Bm25Index();
        var shortChunk = Chunks.Create("kubernetes cluster");
        var longChunk = Chunks.Create("kubernetes and a great many other words that dilute the match considerably");
        index.Add([longChunk, shortChunk]);

        Assert.Equal(shortChunk.Id, index.Search("kubernetes", 10)[0].Chunk.Id);
    }

    [Fact]
    public void Matching_ignores_case_and_punctuation()
    {
        var index = new Bm25Index();
        index.Add([Chunks.Create("Error E1234: Disk FULL!")]);

        Assert.Single(index.Search("disk full e1234", 10));
    }

    [Fact]
    public void Queries_made_only_of_stop_words_match_nothing()
    {
        var index = new Bm25Index();
        index.Add([Chunks.Create("what is the plan")]);

        Assert.Empty(index.Search("what is the", 10));
    }

    [Fact]
    public void Re_adding_a_chunk_replaces_it()
    {
        var index = new Bm25Index();
        var documentId = Guid.NewGuid();
        index.Add([Chunks.Create("old wording", documentId)]);
        index.Add([Chunks.Create("new wording", documentId)]);

        Assert.Equal(1, index.Count);
        Assert.Empty(index.Search("old", 10));
        Assert.Single(index.Search("new", 10));
    }

    [Fact]
    public void Removing_a_document_drops_all_of_its_chunks()
    {
        var index = new Bm25Index();
        var removed = Guid.NewGuid();
        var kept = Guid.NewGuid();
        index.Add([Chunks.Create("shared term one", removed, 0), Chunks.Create("shared term two", removed, 1), Chunks.Create("shared term", kept)]);

        Assert.Equal(2, index.RemoveDocument(removed));

        var hit = Assert.Single(index.Search("shared", 10));
        Assert.Equal(kept, hit.Chunk.DocumentId);
    }

    [Fact]
    public void Search_respects_the_limit()
    {
        var index = new Bm25Index();
        index.Add(Enumerable.Range(0, 10).Select(i => Chunks.Create($"common token {i}")));

        Assert.Equal(3, index.Search("common", 3).Count);
    }

    [Fact]
    public void Section_titles_are_searchable()
    {
        var index = new Bm25Index();
        index.Add([Chunks.Create("Use the portal.", section: "Expenses › Reimbursement")]);

        Assert.Single(index.Search("reimbursement", 10));
    }

    [Fact]
    public void Lists_documents_with_their_chunk_counts()
    {
        var index = new Bm25Index();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        index.Add([Chunks.Create("one two", a, 0, "b.md"), Chunks.Create("three", a, 1, "b.md"), Chunks.Create("four", b, 0, "a.txt")]);

        var documents = index.ListDocuments();

        Assert.Equal(["a.txt", "b.md"], documents.Select(d => d.Name));
        Assert.Equal(2, documents[1].ChunkCount);
        Assert.Equal(a, documents[1].Id);
    }
}
