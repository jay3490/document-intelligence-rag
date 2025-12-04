using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DocumentIntelligence.Core;
using DocumentIntelligence.Core.Documents;
using DocumentIntelligence.Core.Search;
using DocumentIntelligence.Core.Storage;
using DocumentIntelligence.Core.Tenancy;
using DocumentIntelligence.Infrastructure.Ai;
using DocumentIntelligence.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Infrastructure.VectorStores;

/// <summary>
/// Stores chunks as Qdrant points (cosine distance) through the REST API. The collection is
/// created on first use, and payload indexes are ensured on <c>document_id</c> (filtered
/// deletes), <c>tenant_id</c> (marked as the tenant key, so Qdrant groups each tenant's points)
/// and the metadata fields searches filter on.
/// </summary>
public sealed partial class QdrantVectorStore : IVectorStore, IDisposable
{
    public const string HttpClientName = "qdrant";

    private const string Service = "qdrant";
    private const int ScrollPageSize = 256;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<QdrantVectorStore> _logger;
    private readonly string _collection;
    private readonly int _dimensions;
    private readonly SemaphoreSlim _collectionGate = new(1, 1);
    private volatile bool _collectionReady;

    public QdrantVectorStore(
        IHttpClientFactory httpClientFactory,
        IOptions<VectorStoreOptions> storeOptions,
        IOptions<AiOptions> aiOptions,
        ILogger<QdrantVectorStore> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _collection = storeOptions.Value.Qdrant.Collection;
        _dimensions = aiOptions.Value.EmbeddingDimensions;
    }

    public async Task UpsertAsync(IReadOnlyList<EmbeddedChunk> chunks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        if (chunks.Count == 0)
        {
            return;
        }

        await EnsureCollectionAsync(cancellationToken);

        var points = chunks.Select(c => new Point(c.Chunk.Id, c.Vector, ChunkPayload.From(c.Chunk))).ToList();
        await SendAsync<JsonElement>(HttpMethod.Put, $"collections/{_collection}/points?wait=true", new { points }, cancellationToken);
    }

    public async Task<IReadOnlyList<ScoredChunk>> SearchAsync(float[] vector, int limit, SearchFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vector);
        ArgumentNullException.ThrowIfNull(filter);
        await EnsureCollectionAsync(cancellationToken);

        var response = await SendAsync<QueryResult>(
            HttpMethod.Post,
            $"collections/{_collection}/points/query",
            new { query = vector, limit, with_payload = true, filter = QdrantFilters.Build(filter) },
            cancellationToken);

        return response.Points
            .Select(point => new ScoredChunk(ToChunk(point.Id, point.Payload), point.Score))
            .ToList();
    }

    public async Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        await EnsureCollectionAsync(cancellationToken);

        var filter = QdrantFilters.ForDocument(documentId);
        await SendAsync<JsonElement>(HttpMethod.Post, $"collections/{_collection}/points/delete?wait=true", new { filter }, cancellationToken);
    }

    public IAsyncEnumerable<DocumentChunk> ReadAllAsync(CancellationToken cancellationToken) =>
        ScrollAsync(filter: null, cancellationToken);

    public IAsyncEnumerable<DocumentChunk> ReadDocumentAsync(Guid documentId, CancellationToken cancellationToken) =>
        ScrollAsync(QdrantFilters.ForDocument(documentId), cancellationToken);

    public void Dispose() => _collectionGate.Dispose();

    private async IAsyncEnumerable<DocumentChunk> ScrollAsync(object? filter, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await EnsureCollectionAsync(cancellationToken);

        JsonElement? offset = null;
        do
        {
            var page = await SendAsync<ScrollResult>(
                HttpMethod.Post,
                $"collections/{_collection}/points/scroll",
                new ScrollRequest(ScrollPageSize, offset, WithPayload: true, WithVector: false, filter),
                cancellationToken);

            foreach (var point in page.Points)
            {
                yield return ToChunk(point.Id, point.Payload);
            }

            offset = page.NextPageOffset is { ValueKind: not JsonValueKind.Null } next ? next : null;
        }
        while (offset is not null);
    }

    private async Task EnsureCollectionAsync(CancellationToken cancellationToken)
    {
        if (_collectionReady)
        {
            return;
        }

        await _collectionGate.WaitAsync(cancellationToken);
        try
        {
            if (_collectionReady)
            {
                return;
            }

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using (var response = await JsonHttp.SendRawAsync(client, HttpMethod.Get, $"collections/{_collection}", null, Service, cancellationToken))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    await CreateCollectionAsync(cancellationToken);
                }
                else
                {
                    await JsonHttp.EnsureSuccessAsync(response, Service, cancellationToken);
                    var info = await JsonSerializer.DeserializeAsync<QdrantResponse<CollectionInfo>>(
                        await response.Content.ReadAsStreamAsync(cancellationToken), JsonHttp.SnakeCase, cancellationToken);
                    VerifyDimensions(info?.Result);
                }
            }

            // Creating an index that already exists is a no-op, so collections created by an
            // earlier version gain newer indexes as well.
            foreach (var index in QdrantFilters.PayloadIndexes)
            {
                await SendAsync<JsonElement>(HttpMethod.Put, $"collections/{_collection}/index?wait=true", index, cancellationToken);
            }

            _collectionReady = true;
        }
        finally
        {
            _collectionGate.Release();
        }
    }

    private async Task CreateCollectionAsync(CancellationToken cancellationToken)
    {
        await SendAsync<JsonElement>(
            HttpMethod.Put,
            $"collections/{_collection}",
            new { vectors = new { size = _dimensions, distance = "Cosine" } },
            cancellationToken);

        LogCollectionCreated(_logger, _collection, _dimensions);
    }

    private void VerifyDimensions(CollectionInfo? info)
    {
        var size = info?.Config?.Params?.Vectors is { ValueKind: JsonValueKind.Object } vectors &&
                   vectors.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt32(out var parsed)
            ? parsed
            : (int?)null;

        if (size is null)
        {
            throw new UpstreamServiceException(
                Service,
                $"Collection '{_collection}' does not use a single unnamed vector, which this store requires.");
        }

        if (size != _dimensions)
        {
            throw new UpstreamServiceException(
                Service,
                $"Collection '{_collection}' holds {size}-dimensional vectors but the embedding model is configured " +
                $"for {_dimensions}. Use a different collection name or matching embedding dimensions.");
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object body, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var response = await JsonHttp.SendAsync<QdrantResponse<T>>(client, method, path, body, Service, cancellationToken);
        return response.Result is { } result
            ? result
            : throw new UpstreamServiceException(Service, $"Qdrant returned no result for {method} {path}.");
    }

    private static DocumentChunk ToChunk(JsonElement id, ChunkPayload? payload)
    {
        if (payload is null || !Guid.TryParse(id.ToString(), out var chunkId) || !Guid.TryParse(payload.DocumentId, out var documentId))
        {
            throw new UpstreamServiceException(Service, $"Point {id} is missing the chunk payload this store writes.");
        }

        return new DocumentChunk(chunkId, documentId, payload.DocumentName, payload.ChunkIndex, payload.Section, payload.Text, payload.TokenCount)
        {
            TenantId = payload.TenantId ?? TenantId.Default,
            Metadata = new DocumentMetadata { Source = payload.Source, Tags = payload.Tags ?? [], Date = payload.Date },
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created Qdrant collection {Collection} with {Dimensions} dimensions")]
    private static partial void LogCollectionCreated(ILogger logger, string collection, int dimensions);

    private sealed record ChunkPayload(
        string DocumentId,
        string DocumentName,
        int ChunkIndex,
        string? Section,
        string Text,
        int TokenCount,
        string? TenantId,
        string? Source,
        IReadOnlyList<string>? Tags,
        DateTimeOffset? Date)
    {
        public static ChunkPayload From(DocumentChunk chunk) =>
            new(
                chunk.DocumentId.ToString(),
                chunk.DocumentName,
                chunk.ChunkIndex,
                chunk.Section,
                chunk.Text,
                chunk.TokenCount,
                chunk.TenantId,
                chunk.Metadata.Source,
                chunk.Metadata.Tags,
                chunk.Metadata.Date?.ToUniversalTime());
    }

    private sealed record Point(Guid Id, float[] Vector, ChunkPayload Payload);

    private sealed record QdrantResponse<T>(T? Result);

    private sealed record ScoredPoint(JsonElement Id, double Score, ChunkPayload? Payload);

    private sealed record QueryResult(IReadOnlyList<ScoredPoint> Points);

    private sealed record ScrollRequest(int Limit, JsonElement? Offset, bool WithPayload, bool WithVector, object? Filter);

    private sealed record RecordPoint(JsonElement Id, ChunkPayload? Payload);

    private sealed record ScrollResult(IReadOnlyList<RecordPoint> Points, JsonElement? NextPageOffset);

    private sealed record CollectionInfo(CollectionConfig? Config);

    private sealed record CollectionConfig(CollectionParams? Params);

    private sealed record CollectionParams(JsonElement? Vectors);
}
