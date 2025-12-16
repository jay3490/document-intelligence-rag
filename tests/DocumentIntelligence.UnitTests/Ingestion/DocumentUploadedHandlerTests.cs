using System.Security.Cryptography;
using System.Text;
using DocumentIntelligence.Core;
using DocumentIntelligence.Core.Chunking;
using DocumentIntelligence.Core.Embeddings;
using DocumentIntelligence.Core.Ingestion;
using DocumentIntelligence.Core.Messaging;
using DocumentIntelligence.Core.Search;
using DocumentIntelligence.Infrastructure.Offline;
using DocumentIntelligence.Infrastructure.Persistence;
using DocumentIntelligence.Infrastructure.VectorStores;
using DocumentIntelligence.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.UnitTests.Ingestion;

public sealed class DocumentUploadedHandlerTests : IAsyncLifetime
{
    private const int Dimensions = 32;

    private readonly MemoryBlobStore _blobs = new();
    private readonly RecordingPublisher _publisher = new();
    private readonly RecordingQueryCache _cache = new();
    private readonly InMemoryVectorStore _vectors = new(Dimensions);
    private readonly Bm25Index _keywords = new();
    private SqliteJobDatabase _database = null!;
    private EfIngestionJobStore _jobs = null!;

    public async Task InitializeAsync()
    {
        _database = await SqliteJobDatabase.CreateAsync();
        _jobs = _database.CreateStore();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Creates_the_job_ingests_the_blob_and_announces_the_change()
    {
        var message = await UploadAsync("acme", "handbook.md", "# Leave\n\nEmployees get 25 days.", tags: ["hr"]);

        var outcome = await CreateHandler().HandleAsync(message, CancellationToken.None);

        Assert.Equal(UploadOutcome.Completed, outcome);
        var job = (await _jobs.GetAsync(message.JobId, CancellationToken.None))!;
        Assert.Equal(IngestionStatus.Completed, job.Status);
        Assert.Equal(1, job.Attempts);
        Assert.Equal(1, job.ChunkCount);
        Assert.Equal(message.CorrelationId, job.CorrelationId);

        var chunk = Assert.Single(_keywords.Search("leave", 5, new SearchFilter("acme") { Tags = ["hr"] })).Chunk;
        Assert.Equal(message.DocumentId, chunk.DocumentId);
        Assert.Equal(new[] { "acme" }, _cache.InvalidatedTenants);
        var change = Assert.Single(_publisher.IndexChanges);
        Assert.Equal((message.DocumentId, IndexChange.Indexed, message.CorrelationId), (change.DocumentId, change.Change, change.CorrelationId));
    }

    [Fact]
    public async Task A_redelivered_message_for_a_finished_job_is_ignored()
    {
        var message = await UploadAsync("acme", "a.md", "Some text.");
        var embeddings = new CountingEmbeddingClient();
        var handler = CreateHandler(embeddings);
        await handler.HandleAsync(message, CancellationToken.None);

        var outcome = await handler.HandleAsync(message, CancellationToken.None);

        Assert.Equal(UploadOutcome.AlreadyProcessed, outcome);
        Assert.Equal(1, embeddings.Calls);
        Assert.Equal(1, (await _jobs.GetAsync(message.JobId, CancellationToken.None))!.Attempts);
    }

    [Fact]
    public async Task Identical_content_in_the_same_tenant_is_recorded_as_a_duplicate()
    {
        var handler = CreateHandler();
        var first = await UploadAsync("acme", "a.md", "Same words.");
        var copy = await UploadAsync("acme", "b.md", "Same words.");
        var otherTenant = await UploadAsync("globex", "a.md", "Same words.");

        await handler.HandleAsync(first, CancellationToken.None);

        Assert.Equal(UploadOutcome.Duplicate, await handler.HandleAsync(copy, CancellationToken.None));
        Assert.Equal(UploadOutcome.Completed, await handler.HandleAsync(otherTenant, CancellationToken.None));
        var duplicate = (await _jobs.GetAsync(copy.JobId, CancellationToken.None))!;
        Assert.Equal(IngestionStatus.Duplicate, duplicate.Status);
        Assert.Equal(first.DocumentId, duplicate.DuplicateOfDocumentId);
        Assert.Equal(2, _keywords.ListDocuments().Count);
    }

    [Fact]
    public async Task Content_is_ingested_again_once_the_earlier_copy_was_removed()
    {
        var handler = CreateHandler();
        var first = await UploadAsync("acme", "a.md", "Same words.");
        await handler.HandleAsync(first, CancellationToken.None);
        await _jobs.MarkDocumentRemovedAsync(first.DocumentId, CancellationToken.None);

        var again = await UploadAsync("acme", "a.md", "Same words.");

        Assert.Equal(UploadOutcome.Completed, await handler.HandleAsync(again, CancellationToken.None));
    }

    [Fact]
    public async Task Transient_failures_are_retried()
    {
        var message = await UploadAsync("acme", "a.md", "Some text.");
        var embeddings = new CountingEmbeddingClient(failFirst: 2);

        var outcome = await CreateHandler(embeddings, maxAttempts: 3).HandleAsync(message, CancellationToken.None);

        Assert.Equal(UploadOutcome.Completed, outcome);
        Assert.Equal(3, (await _jobs.GetAsync(message.JobId, CancellationToken.None))!.Attempts);
        Assert.Empty(_publisher.DeadLetters);
    }

    [Fact]
    public async Task Exhausted_retries_fail_the_job_and_dead_letter_the_message()
    {
        var message = await UploadAsync("acme", "a.md", "Some text.");
        var embeddings = new CountingEmbeddingClient(failFirst: int.MaxValue);

        var outcome = await CreateHandler(embeddings, maxAttempts: 3).HandleAsync(message, CancellationToken.None);

        Assert.Equal(UploadOutcome.DeadLettered, outcome);
        var job = (await _jobs.GetAsync(message.JobId, CancellationToken.None))!;
        Assert.Equal(IngestionStatus.Failed, job.Status);
        Assert.Equal(3, job.Attempts);
        Assert.Contains("embedding service unavailable", job.Error);
        var deadLetter = Assert.Single(_publisher.DeadLetters);
        Assert.Equal(message, deadLetter.Message);
        Assert.Equal(3, deadLetter.Attempts);
        Assert.Empty(_publisher.IndexChanges);
        Assert.Empty(_keywords.ListDocuments());
    }

    [Fact]
    public async Task A_missing_blob_is_dead_lettered_without_retrying()
    {
        var message = await UploadAsync("acme", "a.md", "Some text.");
        _blobs.Blobs.Clear();

        var outcome = await CreateHandler(maxAttempts: 5).HandleAsync(message, CancellationToken.None);

        Assert.Equal(UploadOutcome.DeadLettered, outcome);
        Assert.Equal(1, Assert.Single(_publisher.DeadLetters).Attempts);
        Assert.Contains("no longer exists", (await _jobs.GetAsync(message.JobId, CancellationToken.None))!.Error);
    }

    [Fact]
    public async Task Content_that_is_not_utf8_is_dead_lettered()
    {
        var message = await UploadAsync("acme", "a.txt", "placeholder");
        _blobs.Blobs[message.BlobName] = [0x48, 0xFF, 0xFE];

        Assert.Equal(UploadOutcome.DeadLettered, await CreateHandler().HandleAsync(message, CancellationToken.None));
        Assert.Contains("not UTF-8", (await _jobs.GetAsync(message.JobId, CancellationToken.None))!.Error);
    }

    [Theory]
    [InlineData(2, 100)]
    [InlineData(3, 200)]
    [InlineData(4, 400)]
    [InlineData(10, 1000)]
    public void Retry_delays_double_up_to_the_maximum(int attempt, int expectedMilliseconds)
    {
        var options = new IngestionOptions { RetryBaseDelayMilliseconds = 100, RetryMaxDelayMilliseconds = 1000 };

        Assert.Equal(TimeSpan.FromMilliseconds(expectedMilliseconds), options.RetryDelay(attempt));
    }

    [Fact]
    public void Dependency_and_io_failures_are_transient_but_bad_input_is_not()
    {
        Assert.True(DocumentUploadedHandler.IsTransient(new UpstreamServiceException("qdrant", "down")));
        Assert.True(DocumentUploadedHandler.IsTransient(new HttpRequestException()));
        Assert.False(DocumentUploadedHandler.IsTransient(new InvalidDataException()));
        Assert.False(DocumentUploadedHandler.IsTransient(new InvalidOperationException()));
    }

    private DocumentUploadedHandler CreateHandler(IEmbeddingClient? embeddings = null, int maxAttempts = 3)
    {
        var pipeline = new IngestionPipeline(
            new TextChunker(new WordTokenCounter(), Options.Create(new ChunkingOptions { MaxTokens = 64, OverlapTokens = 0 })),
            embeddings ?? new HashingEmbeddingClient(Dimensions),
            _vectors,
            _keywords,
            Options.Create(new IngestionOptions()),
            NullLogger<IngestionPipeline>.Instance);

        return new DocumentUploadedHandler(
            _jobs,
            _blobs,
            pipeline,
            _cache,
            _publisher,
            Options.Create(new IngestionOptions { MaxAttempts = maxAttempts, RetryBaseDelayMilliseconds = 0, RetryMaxDelayMilliseconds = 0 }),
            TimeProvider.System,
            NullLogger<DocumentUploadedHandler>.Instance);
    }

    private async Task<DocumentUploaded> UploadAsync(string tenant, string fileName, string content, IReadOnlyList<string>? tags = null)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var documentId = Guid.NewGuid();
        var message = new DocumentUploaded
        {
            JobId = Guid.NewGuid(),
            TenantId = tenant,
            DocumentId = documentId,
            FileName = fileName,
            BlobName = $"{tenant}/{documentId:N}/{fileName}",
            ContentHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            SizeBytes = bytes.Length,
            Tags = tags ?? [],
            UploadedAt = DateTimeOffset.UtcNow,
            CorrelationId = $"corr-{documentId:N}",
        };

        using var stream = new MemoryStream(bytes);
        await _blobs.PutAsync(message.BlobName, stream, CancellationToken.None);
        return message;
    }

    private sealed class CountingEmbeddingClient(int failFirst = 0) : IEmbeddingClient
    {
        private readonly HashingEmbeddingClient _inner = new(Dimensions);

        public int Calls { get; private set; }

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls <= failFirst)
            {
                throw new UpstreamServiceException("embeddings", "embedding service unavailable");
            }

            return _inner.EmbedAsync(texts, cancellationToken);
        }
    }
}
