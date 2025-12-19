using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using DocumentIntelligence.Core;
using DocumentIntelligence.Core.Messaging;
using DocumentIntelligence.Infrastructure.Messaging;
using DocumentIntelligence.Infrastructure.Messaging.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.UnitTests.Messaging;

public sealed class KafkaMessagingTests
{
    private static readonly DocumentUploaded Upload = new()
    {
        JobId = Guid.NewGuid(),
        TenantId = "acme",
        DocumentId = Guid.NewGuid(),
        FileName = "notes.md",
        BlobName = "acme/x/notes.md",
        ContentHash = "ab",
        SizeBytes = 12,
        Tags = ["hr"],
        UploadedAt = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero),
        CorrelationId = "corr-42",
    };

    [Fact]
    public async Task Uploads_are_keyed_by_tenant_and_carry_the_correlation_id()
    {
        var producer = new RecordingProducer();
        var publisher = new KafkaIngestionEventPublisher(producer, Options.Create(new KafkaOptions()));

        await publisher.PublishUploadedAsync(Upload, CancellationToken.None);

        var (topic, record) = Assert.Single(producer.Records);
        Assert.Equal("document-uploaded", topic);
        Assert.Equal("acme", record.Key);
        Assert.Equal("corr-42", record.Headers[MessageHeaders.CorrelationId]);
        Assert.Equal(nameof(DocumentUploaded), record.Headers[MessageHeaders.MessageType]);
        Assert.Equal(Upload with { }, KafkaSerializer.Deserialize<DocumentUploaded>(record.Value), new UploadComparer());
    }

    [Fact]
    public async Task Dead_letters_record_the_error_and_attempts_as_headers()
    {
        var producer = new RecordingProducer();
        var publisher = new KafkaIngestionEventPublisher(producer, Options.Create(new KafkaOptions()));

        await publisher.PublishDeadLetterAsync(new DeadLetteredUpload(Upload, "qdrant down", 4, DateTimeOffset.UnixEpoch), CancellationToken.None);

        var (topic, record) = Assert.Single(producer.Records);
        Assert.Equal("document-uploaded-dlq", topic);
        Assert.Equal("qdrant down", record.Headers[MessageHeaders.Error]);
        Assert.Equal("4", record.Headers[MessageHeaders.Attempts]);
        Assert.Equal(4, KafkaSerializer.Deserialize<DeadLetteredUpload>(record.Value).Attempts);
    }

    [Fact]
    public async Task Index_changes_are_keyed_by_document()
    {
        var producer = new RecordingProducer();
        var publisher = new KafkaIngestionEventPublisher(producer, Options.Create(new KafkaOptions()));
        var change = new DocumentIndexChanged("acme", Upload.DocumentId, IndexChange.Removed, DateTimeOffset.UnixEpoch, "c");

        await publisher.PublishIndexChangedAsync(change, CancellationToken.None);

        var (topic, record) = Assert.Single(producer.Records);
        Assert.Equal("document-index-changed", topic);
        Assert.Equal(Upload.DocumentId.ToString("N"), record.Key);
        Assert.Contains("\"Removed\"", Encoding.UTF8.GetString(record.Value));
        Assert.Equal(change, KafkaSerializer.Deserialize<DocumentIndexChanged>(record.Value));
    }

    [Fact]
    public async Task The_dispatcher_hands_messages_to_the_handler_inside_the_producers_trace()
    {
        using var listener = ListenToTelemetry();
        var producer = new RecordingProducer();
        KafkaRecord record;
        string producerTraceId;
        using (var producing = Telemetry.ActivitySource.StartActivity("produce"))
        {
            producerTraceId = producing!.TraceId.ToString();
            record = KafkaSerializer.ToRecord("acme", Upload, Upload.CorrelationId);
        }

        string? handlerTraceId = null;
        DocumentUploaded? received = null;
        var dispatcher = new KafkaRecordDispatcher<DocumentUploaded>(
            (message, _) =>
            {
                received = message;
                handlerTraceId = Activity.Current?.TraceId.ToString();
                return Task.CompletedTask;
            },
            producer,
            "dlq",
            NullLogger.Instance);

        var result = await dispatcher.DispatchAsync("document-uploaded", record, CancellationToken.None);

        Assert.Equal(DispatchResult.Handled, result);
        Assert.Equal(Upload.JobId, received!.JobId);
        Assert.Equal(producerTraceId, handlerTraceId);
        Assert.Empty(producer.Records);
    }

    [Fact]
    public async Task Unreadable_records_are_parked_on_the_dead_letter_topic()
    {
        var producer = new RecordingProducer();
        var called = false;
        var dispatcher = new KafkaRecordDispatcher<DocumentUploaded>((_, _) => { called = true; return Task.CompletedTask; }, producer, "dlq", NullLogger.Instance);
        var garbage = new KafkaRecord("k", Encoding.UTF8.GetBytes("{not json"), new Dictionary<string, string> { [MessageHeaders.CorrelationId] = "c" });

        var result = await dispatcher.DispatchAsync("document-uploaded", garbage, CancellationToken.None);

        Assert.Equal(DispatchResult.Poisoned, result);
        Assert.False(called);
        var (topic, parked) = Assert.Single(producer.Records);
        Assert.Equal("dlq", topic);
        Assert.Equal(garbage.Value, parked.Value);
        Assert.Equal("c", parked.Headers[MessageHeaders.CorrelationId]);
        Assert.StartsWith("Unreadable DocumentUploaded", parked.Headers[MessageHeaders.Error]);
    }

    [Fact]
    public async Task A_failing_handler_asks_for_redelivery()
    {
        var dispatcher = new KafkaRecordDispatcher<DocumentUploaded>(
            (_, _) => throw new InvalidOperationException("database unavailable"), new RecordingProducer(), "dlq", NullLogger.Instance);

        var result = await dispatcher.DispatchAsync("document-uploaded", KafkaSerializer.ToRecord("acme", Upload, null), CancellationToken.None);

        Assert.Equal(DispatchResult.Failed, result);
    }

    [Fact]
    public void Topic_names_must_be_distinct()
    {
        var options = new KafkaOptions { Topics = { DeadLetter = "document-uploaded" } };
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        Assert.False(System.ComponentModel.DataAnnotations.Validator.TryValidateObject(options, new(options), results, validateAllProperties: true));
    }

    [Theory]
    [InlineData(IngestionHostRole.Api, typeof(IndexChangedKafkaConsumer))]
    [InlineData(IngestionHostRole.Worker, typeof(DocumentUploadedKafkaConsumer))]
    public void Kafka_hosts_get_the_consumer_for_their_role(IngestionHostRole role, Type consumer)
    {
        var services = new ServiceCollection();

        services.AddIngestionMessaging(KafkaConfiguration(), role);

        var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)).Select(d => d.ImplementationType).ToList();
        Assert.Contains(consumer, hosted);
        Assert.Contains(typeof(KafkaTopicInitializer), hosted);
        Assert.Contains(services, d => d.ServiceType == typeof(IIngestionEventPublisher) && d.ImplementationType == typeof(KafkaIngestionEventPublisher));
    }

    [Fact]
    public void Publishers_get_no_consumer()
    {
        var services = new ServiceCollection();

        services.AddIngestionMessaging(KafkaConfiguration(), IngestionHostRole.Publisher);

        Assert.DoesNotContain(services, d => d.ImplementationType?.IsSubclassOf(typeof(BackgroundService)) == true);
    }

    [Fact]
    public void The_worker_refuses_the_in_process_transport()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ingestion:Transport"] = "InProcess" }).Build();

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddIngestionMessaging(configuration, IngestionHostRole.Worker));
    }

    private static IConfiguration KafkaConfiguration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ingestion:Transport"] = "Kafka" }).Build();

    private static ActivityListener ListenToTelemetry()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed class RecordingProducer : IMessageProducer
    {
        public ConcurrentQueue<(string Topic, KafkaRecord Record)> Records { get; } = new();

        public Task ProduceAsync(string topic, KafkaRecord record, CancellationToken cancellationToken)
        {
            Records.Enqueue((topic, record));
            return Task.CompletedTask;
        }
    }

    private sealed class UploadComparer : IEqualityComparer<DocumentUploaded>
    {
        public bool Equals(DocumentUploaded? x, DocumentUploaded? y) =>
            x is not null && y is not null &&
            (x with { Tags = [] }) == (y with { Tags = [] }) &&
            x.Tags.SequenceEqual(y.Tags);

        public int GetHashCode(DocumentUploaded obj) => obj.JobId.GetHashCode();
    }
}
