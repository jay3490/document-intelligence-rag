using Confluent.Kafka;
using DocumentIntelligence.Core.Ingestion;
using DocumentIntelligence.Core.Messaging;
using DocumentIntelligence.Core.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Infrastructure.Messaging.Kafka;

/// <summary>The ingestion worker's consumer: one shared group, each upload handled by one member.</summary>
public sealed class DocumentUploadedKafkaConsumer(
    DocumentUploadedHandler handler,
    IEnumerable<IIngestionReadiness> readiness,
    IOptions<KafkaOptions> options,
    IMessageProducer producer,
    ILogger<DocumentUploadedKafkaConsumer> logger)
    : KafkaConsumerService<DocumentUploaded>(options, producer, logger)
{
    protected override string Topic => Options.Topics.DocumentUploaded;

    protected override string GroupId => Options.IngestionConsumerGroup;

    protected override async Task BeforeConsumingAsync(CancellationToken cancellationToken)
    {
        foreach (var gate in readiness)
        {
            await gate.WaitUntilReadyAsync(cancellationToken);
        }
    }

    protected override Task HandleAsync(DocumentUploaded message, CancellationToken cancellationToken) =>
        handler.HandleAsync(message, cancellationToken);
}

/// <summary>
/// Keeps an API replica's in-memory keyword index in step with documents ingested or removed
/// elsewhere. Each replica uses its own consumer group and starts from the newest record, since
/// anything older is already covered by the rebuild at start-up.
/// </summary>
public sealed class IndexChangedKafkaConsumer(
    KeywordIndexSynchronizer synchronizer,
    IOptions<KafkaOptions> options,
    IMessageProducer producer,
    ILogger<IndexChangedKafkaConsumer> logger)
    : KafkaConsumerService<DocumentIndexChanged>(options, producer, logger)
{
    private readonly string _groupId = $"{options.Value.IndexSyncConsumerGroupPrefix}-{Environment.MachineName.ToLowerInvariant()}-{Guid.NewGuid():N}";

    protected override string Topic => Options.Topics.IndexChanged;

    protected override string GroupId => _groupId;

    protected override AutoOffsetReset OffsetReset => AutoOffsetReset.Latest;

    protected override Task HandleAsync(DocumentIndexChanged message, CancellationToken cancellationToken) =>
        synchronizer.ApplyAsync(message, cancellationToken);
}
