using System.Net;
using System.Text.Json;
using DocumentIntelligence.Core;
using DocumentIntelligence.Core.Documents;
using DocumentIntelligence.Core.Search;
using DocumentIntelligence.Core.Storage;
using DocumentIntelligence.Core.Tenancy;
using DocumentIntelligence.Infrastructure.Ai;
using DocumentIntelligence.Infrastructure.VectorStores;
using DocumentIntelligence.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.UnitTests.Infrastructure;

public sealed class QdrantVectorStoreTests
{
    private const string Ok = """{"status":"ok","result":{"operation_id":1,"status":"completed"}}""";
    private const int SetupIndexRequests = 5;

    private static readonly SearchFilter DefaultTenant = new(TenantId.Default);

    [Fact]
    public async Task Creates_the_collection_and_payload_indexes_on_first_use()
    {
        var handler = new StubHttpHandler(request => request.Method == HttpMethod.Get
            ? StubHttpHandler.Json("""{"status":{"error":"Not found"}}""", HttpStatusCode.NotFound)
            : StubHttpHandler.Json(Ok));
        var store = CreateStore(handler, dimensions: 3);
        var chunk = Chunks.Create("hello", section: "Intro") with
        {
            TenantId = "acme",
            Metadata = new DocumentMetadata { Source = "wiki", Tags = ["HR", "policy"], Date = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero) },
        };

        await store.UpsertAsync([new EmbeddedChunk(chunk, [0.1f, 0.2f, 0.3f])], CancellationToken.None);
        await store.UpsertAsync([new EmbeddedChunk(chunk, [0.1f, 0.2f, 0.3f])], CancellationToken.None);

        Assert.Equal(
            new[] { "GET /collections/docs", "PUT /collections/docs" }
                .Concat(Enumerable.Repeat("PUT /collections/docs/index?wait=true", SetupIndexRequests))
                .Concat(Enumerable.Repeat("PUT /collections/docs/points?wait=true", 2)),
            handler.Requests.Select(r => $"{r.Method} {r.PathAndQuery}"));

        using var create = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.Equal(3, create.RootElement.GetProperty("vectors").GetProperty("size").GetInt32());
        Assert.Equal("Cosine", create.RootElement.GetProperty("vectors").GetProperty("distance").GetString());

        var indexedFields = handler.Requests.Skip(2).Take(SetupIndexRequests)
            .Select(r => JsonDocument.Parse(r.Body!).RootElement.GetProperty("field_name").GetString());
        Assert.Equal(new[] { "document_id", "tenant_id", "tags", "source", "date" }, indexedFields);

        using var upsert = JsonDocument.Parse(handler.Requests[2 + SetupIndexRequests].Body!);
        var point = upsert.RootElement.GetProperty("points")[0];
        var payload = point.GetProperty("payload");
        Assert.Equal(chunk.Id.ToString(), point.GetProperty("id").GetString());
        Assert.Equal(chunk.DocumentId.ToString(), payload.GetProperty("document_id").GetString());
        Assert.Equal("Intro", payload.GetProperty("section").GetString());
        Assert.Equal("acme", payload.GetProperty("tenant_id").GetString());
        Assert.Equal("wiki", payload.GetProperty("source").GetString());
        Assert.Equal(new[] { "hr", "policy" }, payload.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
        Assert.StartsWith("2024-03-01T00:00:00", payload.GetProperty("date").GetString());
    }

    [Fact]
    public async Task Refuses_a_collection_with_a_different_vector_size()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(
            """{"status":"ok","result":{"config":{"params":{"vectors":{"size":1536,"distance":"Cosine"}}}}}"""));
        var store = CreateStore(handler, dimensions: 512);

        var error = await Assert.ThrowsAsync<UpstreamServiceException>(() =>
            store.SearchAsync(new float[512], 5, DefaultTenant, CancellationToken.None));

        Assert.Contains("1536", error.Message);
        Assert.Contains("512", error.Message);
    }

    [Fact]
    public async Task Search_maps_scored_points_back_to_chunks()
    {
        var chunk = Chunks.Create("Leave is 25 days.", name: "handbook.md", section: "Leave", index: 4);
        var handler = CollectionThen(_ => Result(new { points = new[] { new { id = chunk.Id, version = 1, score = 0.87, payload = Payload(chunk) } } }));
        var store = CreateStore(handler, dimensions: 2);

        var hit = Assert.Single(await store.SearchAsync([1f, 0f], 3, DefaultTenant, CancellationToken.None));

        Assert.Equal(chunk, hit.Chunk);
        Assert.Equal(0.87, hit.Score, precision: 6);
        using var query = JsonDocument.Parse(handler.Requests.Last().Body!);
        Assert.Equal(3, query.RootElement.GetProperty("limit").GetInt32());
        Assert.True(query.RootElement.GetProperty("with_payload").GetBoolean());
    }

    [Fact]
    public async Task Search_always_filters_by_tenant()
    {
        var handler = CollectionThen(_ => Result(new { points = Array.Empty<object>() }));
        var store = CreateStore(handler, dimensions: 2);

        await store.SearchAsync([1f, 0f], 3, new SearchFilter("acme"), CancellationToken.None);

        using var query = JsonDocument.Parse(handler.Requests.Last().Body!);
        var must = Assert.Single(query.RootElement.GetProperty("filter").GetProperty("must").EnumerateArray());
        Assert.Equal("tenant_id", must.GetProperty("key").GetString());
        Assert.Equal("acme", must.GetProperty("match").GetProperty("value").GetString());
    }

    [Fact]
    public async Task Search_translates_metadata_conditions_into_payload_filters()
    {
        var handler = CollectionThen(_ => Result(new { points = Array.Empty<object>() }));
        var store = CreateStore(handler, dimensions: 2);
        var filter = new SearchFilter("acme")
        {
            Tags = ["Policy", "hr"],
            Source = "wiki",
            From = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.FromHours(2)),
        };

        await store.SearchAsync([1f, 0f], 3, filter, CancellationToken.None);

        using var query = JsonDocument.Parse(handler.Requests.Last().Body!);
        var must = query.RootElement.GetProperty("filter").GetProperty("must").EnumerateArray().ToList();
        Assert.Equal(new[] { "tenant_id", "tags", "source", "date" }, must.Select(m => m.GetProperty("key").GetString()));
        Assert.Equal(new[] { "hr", "policy" }, must[1].GetProperty("match").GetProperty("any").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal("wiki", must[2].GetProperty("match").GetProperty("value").GetString());
        var range = must[3].GetProperty("range");
        Assert.StartsWith("2024-01-01T00:00:00", range.GetProperty("gte").GetString());
        Assert.StartsWith("2024-12-30T22:00:00", range.GetProperty("lte").GetString());
    }

    [Fact]
    public async Task Search_with_only_a_lower_date_bound_omits_the_upper_bound()
    {
        var handler = CollectionThen(_ => Result(new { points = Array.Empty<object>() }));
        var store = CreateStore(handler, dimensions: 2);

        await store.SearchAsync([1f, 0f], 3, new SearchFilter("acme") { From = DateTimeOffset.UnixEpoch }, CancellationToken.None);

        using var query = JsonDocument.Parse(handler.Requests.Last().Body!);
        var range = query.RootElement.GetProperty("filter").GetProperty("must")[1].GetProperty("range");
        Assert.True(range.TryGetProperty("gte", out _));
        Assert.False(range.TryGetProperty("lte", out _));
    }

    [Fact]
    public async Task Points_carry_tenant_and_metadata_back_into_chunks()
    {
        var chunk = Chunks.Create("Tagged.", index: 1) with
        {
            TenantId = "acme",
            Metadata = new DocumentMetadata { Source = "wiki", Tags = ["hr"], Date = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero) },
        };
        var handler = CollectionThen(_ => Result(new { points = new[] { new { id = chunk.Id, score = 0.5, payload = Payload(chunk) } } }));
        var store = CreateStore(handler, dimensions: 2);

        var hit = Assert.Single(await store.SearchAsync([1f, 0f], 3, new SearchFilter("acme"), CancellationToken.None));

        Assert.Equal(chunk, hit.Chunk);
    }

    [Fact]
    public async Task Deletes_a_document_with_a_payload_filter()
    {
        var handler = CollectionThen(_ => StubHttpHandler.Json(Ok));
        var store = CreateStore(handler, dimensions: 2);
        var documentId = Guid.NewGuid();

        await store.DeleteDocumentAsync(documentId, CancellationToken.None);

        var request = handler.Requests.Last();
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
        var handler = CollectionThen(request =>
        {
            var hasOffset = JsonDocument.Parse(request.Body!).RootElement.TryGetProperty("offset", out _);
            return hasOffset ? ScrollPage(second, next: null) : ScrollPage(first, next: second.Id);
        });
        var store = CreateStore(handler, dimensions: 2);

        var chunks = new List<DocumentChunk>();
        await foreach (var chunk in store.ReadAllAsync(CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(new[] { first, second }, chunks);
        Assert.Equal(2, handler.Requests.Count(r => r.PathAndQuery.EndsWith("/scroll", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Reads_one_document_with_a_scroll_filter()
    {
        var chunk = Chunks.Create("one", index: 0);
        var handler = CollectionThen(_ => ScrollPage(chunk, next: null));
        var store = CreateStore(handler, dimensions: 2);

        var chunks = new List<DocumentChunk>();
        await foreach (var read in store.ReadDocumentAsync(chunk.DocumentId, CancellationToken.None))
        {
            chunks.Add(read);
        }

        Assert.Equal(chunk, Assert.Single(chunks));
        using var body = JsonDocument.Parse(handler.Requests.Last().Body!);
        var condition = body.RootElement.GetProperty("filter").GetProperty("must")[0];
        Assert.Equal(chunk.DocumentId.ToString(), condition.GetProperty("match").GetProperty("value").GetString());
    }

    private static QdrantVectorStore CreateStore(StubHttpHandler handler, int dimensions) =>
        new(
            new StubHttpClientFactory(handler, "http://qdrant:6333/"),
            Options.Create(new VectorStoreOptions { Provider = VectorStoreProvider.Qdrant, Qdrant = { Collection = "docs" } }),
            Options.Create(new AiOptions { EmbeddingDimensions = dimensions }),
            NullLogger<QdrantVectorStore>.Instance);

    /// <summary>Answers the collection lookup with a 2-dimensional collection and index creation with OK.</summary>
    private static StubHttpHandler CollectionThen(Func<RecordedRequest, HttpResponseMessage> respond) =>
        new(request =>
            request.Method == HttpMethod.Get ? CollectionInfo(2)
            : request.PathAndQuery.Contains("/index", StringComparison.Ordinal) ? StubHttpHandler.Json(Ok)
            : respond(request));

    private static HttpResponseMessage CollectionInfo(int size) =>
        Result(new { config = new { @params = new { vectors = new { size, distance = "Cosine" } } } });

    private static HttpResponseMessage ScrollPage(DocumentChunk chunk, Guid? next) =>
        Result(new { next_page_offset = next, points = new[] { new { id = chunk.Id, payload = Payload(chunk) } } });

    private static HttpResponseMessage Result(object result) =>
        StubHttpHandler.Json(JsonSerializer.Serialize(new { status = "ok", result }));

    private static object Payload(DocumentChunk chunk) => new
    {
        document_id = chunk.DocumentId,
        document_name = chunk.DocumentName,
        chunk_index = chunk.ChunkIndex,
        section = chunk.Section,
        text = chunk.Text,
        token_count = chunk.TokenCount,
        tenant_id = chunk.TenantId,
        source = chunk.Metadata.Source,
        tags = chunk.Metadata.Tags,
        date = chunk.Metadata.Date,
    };
}
