using DocumentIntelligence.Core.Answering;
using DocumentIntelligence.Core.Chat;
using DocumentIntelligence.Core.Search;
using DocumentIntelligence.Core.Tenancy;
using DocumentIntelligence.Infrastructure.Offline;
using DocumentIntelligence.Infrastructure.VectorStores;
using DocumentIntelligence.UnitTests.TestDoubles;

namespace DocumentIntelligence.UnitTests.Infrastructure;

public sealed class OfflineProviderTests
{
    [Fact]
    public async Task Hashing_embeddings_are_deterministic_and_unit_length()
    {
        var client = new HashingEmbeddingClient(128);

        var first = await client.EmbedAsync(["Quarterly revenue grew"], CancellationToken.None);
        var second = await new HashingEmbeddingClient(128).EmbedAsync(["Quarterly revenue grew"], CancellationToken.None);

        Assert.Equal(first[0], second[0]);
        Assert.Equal(128, first[0].Length);
        Assert.Equal(1.0, Math.Sqrt(first[0].Sum(v => (double)v * v)), precision: 5);
    }

    [Fact]
    public async Task Hashing_embeddings_place_overlapping_texts_closer_than_unrelated_ones()
    {
        var vectors = await new HashingEmbeddingClient(256).EmbedAsync(
            ["How do I reset my password?", "Password resets are done from the login page.", "The cafeteria serves lunch at noon."],
            CancellationToken.None);

        Assert.True(Cosine(vectors[0], vectors[1]) > Cosine(vectors[0], vectors[2]));
    }

    [Fact]
    public async Task Text_without_terms_still_gets_a_valid_vector()
    {
        var vector = (await new HashingEmbeddingClient(16).EmbedAsync(["the of and"], CancellationToken.None))[0];

        Assert.Equal(1f, vector[0]);
        Assert.Equal(1.0, vector.Sum(v => (double)v * v), precision: 6);
    }

    [Fact]
    public async Task Extractive_answers_quote_the_best_matching_sentence_with_its_citation()
    {
        var prompt = GroundedPrompt.Build("When does the office open?",
        [
            new RetrievedChunk(Chunks.Create("Parking is free. Visitors sign in at reception."), 0.1, 1, 1),
            new RetrievedChunk(Chunks.Create("The office opens at 8am. It closes at 6pm."), 0.1, 2, 2),
        ]);

        var answer = await new ExtractiveChatClient().CompleteAsync(prompt, CancellationToken.None);

        Assert.Equal("The office opens at 8am. [2]", answer);
        Assert.Equal(new[] { 2 }, CitationParser.Parse(answer, 2));
    }

    [Fact]
    public async Task Extractive_answers_admit_when_nothing_matches()
    {
        var prompt = GroundedPrompt.Build("What is the refund policy?",
            [new RetrievedChunk(Chunks.Create("The office opens at 8am."), 0.1, 1, 1)]);

        var answer = await new ExtractiveChatClient().CompleteAsync(prompt, CancellationToken.None);

        Assert.Equal(ExtractiveChatClient.NotCoveredAnswer, answer);
    }

    [Fact]
    public async Task Extractive_client_rejects_prompts_it_did_not_build()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new ExtractiveChatClient().CompleteAsync([new ChatMessage(ChatRole.User, "hello")], CancellationToken.None));
    }

    [Fact]
    public async Task In_memory_store_ranks_by_cosine_similarity_and_deletes_by_document()
    {
        var store = new InMemoryVectorStore(2);
        var near = Chunks.Create("near");
        var far = Chunks.Create("far");
        await store.UpsertAsync([new(far, [0f, 1f]), new(near, [1f, 0.1f])], CancellationToken.None);

        var hits = await store.SearchAsync([1f, 0f], 10, new SearchFilter(TenantId.Default), CancellationToken.None);
        Assert.Equal(new[] { near.Id, far.Id }, hits.Select(h => h.Chunk.Id));
        Assert.Equal(0.0, hits[1].Score, precision: 6);

        await store.DeleteDocumentAsync(near.DocumentId, CancellationToken.None);
        Assert.Equal(far.Id, Assert.Single(await store.SearchAsync([1f, 0f], 10, new SearchFilter(TenantId.Default), CancellationToken.None)).Chunk.Id);
    }

    [Fact]
    public async Task In_memory_store_rejects_vectors_of_the_wrong_size()
    {
        var store = new InMemoryVectorStore(3);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.UpsertAsync([new(Chunks.Create("x"), [1f, 0f])], CancellationToken.None));
    }

    private static double Cosine(float[] a, float[] b) => a.Zip(b, (x, y) => (double)x * y).Sum();
}
