using System.Collections.Concurrent;
using System.Text;
using DocumentIntelligence.Core.Ingestion;
using DocumentIntelligence.Core.Messaging;
using DocumentIntelligence.Core.Storage;
using DocumentIntelligence.Functions.Drops;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace DocumentIntelligence.Functions.Tests;

public sealed class BlobDropHandlerTests
{
    private static readonly DateTimeOffset Now = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly MemoryBlobStore _blobs = new();
    private readonly RecordingPublisher _publisher = new();

    [Fact]
    public async Task Publishes_a_dropped_file_for_its_tenant()
    {
        var result = await CreateHandler().HandleAsync(Drop("acme/policies/leave.md", "# Leave\n\n25 days."), "invocation-1", CancellationToken.None);

        Assert.Equal(DropOutcome.Published, result.Outcome);
        var message = Assert.Single(_publisher.Uploaded);
        Assert.Equal(result.JobId, message.JobId);
        Assert.Equal("acme", message.TenantId);
        Assert.Equal("leave.md", message.FileName);
        Assert.Equal("invocation-1", message.CorrelationId);
        Assert.Equal(BlobDropHandler.DefaultSource, message.Source);
        Assert.Equal(Now, message.Date);
        Assert.Equal(Now, message.UploadedAt);
        Assert.Equal(64, message.ContentHash.Length);
        Assert.Equal($"acme/{message.DocumentId:N}/leave.md", message.BlobName);
        Assert.Equal("# Leave\n\n25 days.", Encoding.UTF8.GetString(_blobs.Blobs[message.BlobName]));
    }

    [Fact]
    public async Task A_repeated_trigger_for_the_same_content_reuses_the_job()
    {
        var handler = CreateHandler();

        var first = await handler.HandleAsync(Drop("acme/a.md", "text"), "1", CancellationToken.None);
        var again = await handler.HandleAsync(Drop("acme/a.md", "text"), "2", CancellationToken.None);
        var changed = await handler.HandleAsync(Drop("acme/a.md", "new text"), "3", CancellationToken.None);

        Assert.Equal(first.JobId, again.JobId);
        Assert.NotEqual(first.JobId, changed.JobId);
        Assert.Equal(2, _publisher.Uploaded.Select(m => m.DocumentId).Distinct().Count());
    }

    [Fact]
    public async Task Blob_metadata_becomes_document_metadata()
    {
        var metadata = new Dictionary<string, string> { ["Tags"] = "HR, policy", ["source"] = "sharepoint", ["date"] = "2023-12-24" };

        await CreateHandler().HandleAsync(Drop("acme/a.md", "text", metadata), "1", CancellationToken.None);

        var message = Assert.Single(_publisher.Uploaded);
        Assert.Equal(new[] { "hr", "policy" }, message.Tags);
        Assert.Equal("sharepoint", message.Source);
        Assert.Equal(new DateTimeOffset(2023, 12, 24, 0, 0, 0, TimeSpan.Zero), message.Date);
    }

    [Theory]
    [InlineData("loose-file.md", "text", "valid tenant folder")]
    [InlineData("Bad Tenant/a.md", "text", "valid tenant folder")]
    [InlineData("acme/report.pdf", "text", "Unsupported file type")]
    [InlineData("acme/empty.txt", "  \n ", "no text")]
    public async Task Invalid_drops_are_rejected_without_publishing(string path, string content, string reason)
    {
        var result = await CreateHandler().HandleAsync(Drop(path, content), "1", CancellationToken.None);

        Assert.Equal(DropOutcome.Rejected, result.Outcome);
        Assert.Contains(reason, result.Reason);
        Assert.Empty(_publisher.Uploaded);
        Assert.Empty(_blobs.Blobs);
    }

    [Fact]
    public async Task Binary_and_oversized_files_are_rejected()
    {
        var handler = CreateHandler(maxBytes: 10);

        var binary = await handler.HandleAsync(new BlobDrop("acme/a.txt", [0x48, 0xFF, 0xFE], new Dictionary<string, string>()), "1", CancellationToken.None);
        var large = await handler.HandleAsync(Drop("acme/b.txt", "this is more than ten bytes"), "2", CancellationToken.None);

        Assert.Contains("not UTF-8", binary.Reason);
        Assert.Contains("limit is 10 bytes", large.Reason);
        Assert.Empty(_publisher.Uploaded);
    }

    private BlobDropHandler CreateHandler(int maxBytes = 1024 * 1024) =>
        new(
            _blobs,
            _publisher,
            Options.Create(new IngestionOptions { MaxDocumentBytes = maxBytes }),
            new FakeTimeProvider(Now),
            NullLogger<BlobDropHandler>.Instance);

    private static BlobDrop Drop(string path, string content, Dictionary<string, string>? metadata = null) =>
        new(path, Encoding.UTF8.GetBytes(content), metadata ?? []);

    private sealed class MemoryBlobStore : IBlobStore
    {
        public ConcurrentDictionary<string, byte[]> Blobs { get; } = new();

        public async Task PutAsync(string name, Stream content, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            Blobs[name] = buffer.ToArray();
        }

        public Task<Stream?> OpenReadAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult<Stream?>(Blobs.TryGetValue(name, out var bytes) ? new MemoryStream(bytes) : null);

        public Task DeleteAsync(string name, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingPublisher : IIngestionEventPublisher
    {
        public ConcurrentQueue<DocumentUploaded> Uploaded { get; } = new();

        public Task PublishUploadedAsync(DocumentUploaded message, CancellationToken cancellationToken)
        {
            Uploaded.Enqueue(message);
            return Task.CompletedTask;
        }

        public Task PublishDeadLetterAsync(DeadLetteredUpload message, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishIndexChangedAsync(DocumentIndexChanged message, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
