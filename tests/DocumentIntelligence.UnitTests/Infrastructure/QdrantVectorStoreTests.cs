using System.Net;
using System.Text.Json;
using DocumentIntelligence.Core;
using DocumentIntelligence.Core.Storage;
using DocumentIntelligence.Infrastructure.Ai;
using DocumentIntelligence.Infrastructure.VectorStores;
using DocumentIntelligence.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.UnitTests.Infrastructure;

public sealed class QdrantVectorStoreTests
{
    private const string Ok = """{"status":"ok","result":{"operation_id":1,"status":"completed"}}""";

    [Fact]
    public async Task Creates_the_collection_and_document_index_on_first_use()
    {
        var handler = new StubHttpHandler(request => request.Method == HttpMethod.Get
            ? StubHttpHandler.Json("""{"status":{"error":"Not found"}}""", HttpStatusCode.NotFound)
            : StubHttpHandler.Json(Ok));
        var store = CreateStore(handler, dimensions: 3);
        var chunk = Chunks.Create("hello", section: "Intro");

        await store.UpsertAsync([new EmbeddedChunk(chunk, [0.1f, 0.2f, 0.3f])], CancellationToken.None);
        await store.UpsertAsync([new EmbeddedChunk(chunk, [0.1f, 0.2f, 0.3f])], CancellationToken.None);

        Assert.Equal(
            new[]
            {
                "GET /collections/docs",
                "PUT /collections/docs",
                "PUT /collections/docs/index?wait=true",
                "PUT /collections/docs/points?wait=true",
                "PUT /collections/docs/points?wait=true",
            },
            handler.Requests.Select(r => $"{r.Method} {r.PathAndQuery}"));

        using var create = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.Equal(3, create.RootElement.GetProperty("vectors").GetProperty("size").GetInt32());
        Assert.Equal("Cosine", create.RootElement.GetProperty("vectors").GetProperty("distance").GetString());

        using var upsert = JsonDocument.Parse(handler.Requests[3].Body!);
        var point = upsert.RootElement.GetProperty("points")[0];
        Assert.Equal(chunk.Id.ToString(), point.GetProperty("id").GetString());
        Assert.Equal(chunk.DocumentId.ToString(), point.GetProperty("payload").GetProperty("document_id").GetString());
        Assert.Equal("Intro", point.GetProperty("payload").GetProperty("section").GetString());
    }

    [Fact]
    public async Task Refuses_a_collection_with_a_different_vector_size()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(
            """{"status":"ok","result":{"config":{"params":{"vectors":{"size":1536,"distance":"Cosine"}}}}}"""));
        var store = CreateStore(handler, dimensions: 512);

        var error = await Assert.ThrowsAsync<UpstreamServiceException>(() =>
            store.SearchAsync(new float[512], 5, CancellationToken.None));

        Assert.Contains("1536", error.Message);
        Assert.Contains("512", error.Message);
    }

    [Fact]
    public async Task Search_maps_scored_points_back_to_chunks()
    {
        var chunk = Chunks.Create("Leave is 25 days.", name: "handbook.md", section: "Leave", index: 4);
        var handler = new StubHttpHandler(request => request.Method == HttpMethod.Get
            ? CollectionInfo(2)
            : Result(new { points = new[] { new { id = chunk.Id, version = 1, score = 0.87, payload = Payload(chunk) } } }));
        var store = CreateStore(handler, dimensions: 2);

        var hit = Assert.Single(await store.SearchAsync([1f, 0f], 3, CancellationToken.None));

        Assert.Equal(chunk, hit.Chunk);
        Assert.Equal(0.87, hit.Score, precision: 6);
        using var query = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.Equal(3, query.RootElement.GetProperty("limit").GetInt32());
        Assert.True(query.RootElement.GetProperty("with_payload").GetBoolean());
    }

    [Fact]
    public async Task Deletes_a_document_with_a_payload_filter()
    {
        var handler = new StubHttpHandler(request => request.Method == HttpMethod.Get ? CollectionInfo(2) : StubHttpHandler.Json(Ok));
        var store = CreateStore(handler, dimensions: 2);
        var documentId = Guid.NewGuid();

        await store.DeleteDocumentAsync(documentId, CancellationToken.None);

        var request = handler.Requests[1];
        Assert.Equal("/collections/docs/points/delete?wait=true", request.PathAndQuery);
        using var body = JsonDocument.Parse(request.Body!);
        var condition = body.RootElement.GetProperty("filter").GetProperty("must")[0];
        Assert.Equal("document_id", condition.GetProperty("key").GetString());
        Assert.Equal(documentId.ToString(), condition.GetProperty("match").GetProperty("value").GetString());
    }

    [Fact]
    public async Task Reads_all_points_by_following_scroll_offsets()
    {
        var first = Chunks.Create("one", index: 0);
        var second = Chunks.Create("two", index: 1);
        var handler = new StubHttpHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return CollectionInfo(2);
            }

            var hasOffset = JsonDocument.Parse(request.Body!).RootElement.TryGetProperty("offset", out _);
            return hasOffset ? ScrollPage(second, next: null) : ScrollPage(first, next: second.Id);
        });
        var store = CreateStore(handler, dimensions: 2);

        var chunks = new List<Core.Documents.DocumentChunk>();
        await foreach (var chunk in store.ReadAllAsync(CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(new[] { first, second }, chunks);
        Assert.Equal(3, handler.Requests.Count);
    }

    private static QdrantVectorStore CreateStore(StubHttpHandler handler, int dimensions) =>
        new(
            new StubHttpClientFactory(handler, "http://qdrant:6333/"),
            Options.Create(new VectorStoreOptions { Provider = VectorStoreProvider.Qdrant, Qdrant = { Collection = "docs" } }),
            Options.Create(new AiOptions { EmbeddingDimensions = dimensions }),
            NullLogger<QdrantVectorStore>.Instance);

    private static HttpResponseMessage CollectionInfo(int size) =>
        Result(new { config = new { @params = new { vectors = new { size, distance = "Cosine" } } } });

    private static HttpResponseMessage ScrollPage(Core.Documents.DocumentChunk chunk, Guid? next) =>
        Result(new { next_page_offset = next, points = new[] { new { id = chunk.Id, payload = Payload(chunk) } } });

    private static HttpResponseMessage Result(object result) =>
        StubHttpHandler.Json(JsonSerializer.Serialize(new { status = "ok", result }));

    private static object Payload(Core.Documents.DocumentChunk chunk) => new
    {
        document_id = chunk.DocumentId,
        document_name = chunk.DocumentName,
        chunk_index = chunk.ChunkIndex,
        section = chunk.Section,
        text = chunk.Text,
        token_count = chunk.TokenCount,
    };
}
