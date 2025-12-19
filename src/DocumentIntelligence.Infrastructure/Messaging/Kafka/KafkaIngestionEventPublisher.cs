using System.Globalization;
using DocumentIntelligence.Core.Messaging;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Infrastructure.Messaging.Kafka;

/// <summary>
/// Publishes ingestion messages to Kafka. Uploads and dead letters are keyed by tenant, so one
/// tenant's uploads stay in order on one partition; index changes are keyed by document.
/// </summary>
public sealed class KafkaIngestionEventPublisher(IMessageProducer producer, IOptions<KafkaOptions> options) : IIngestionEventPublisher
{
    private readonly KafkaTopics _topics = options.Value.Topics;

    public Task PublishUploadedAsync(DocumentUploaded message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return producer.ProduceAsync(
            _topics.DocumentUploaded,
            KafkaSerializer.ToRecord(message.TenantId, message, message.CorrelationId),
            cancellationToken);
    }

    public Task PublishDeadLetterAsync(DeadLetteredUpload message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var headers = new Dictionary<string, string>
        {
            [MessageHeaders.Error] = message.Error,
            [MessageHeaders.Attempts] = message.Attempts.ToString(CultureInfo.InvariantCulture),
        };

        return producer.ProduceAsync(
            _topics.DeadLetter,
            KafkaSerializer.ToRecord(message.Message.TenantId, message, message.Message.CorrelationId, headers),
            cancellationToken);
    }

    public Task PublishIndexChangedAsync(DocumentIndexChanged message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return producer.ProduceAsync(
            _topics.IndexChanged,
            KafkaSerializer.ToRecord(message.DocumentId.ToString("N"), message, message.CorrelationId),
            cancellationToken);
    }
}
