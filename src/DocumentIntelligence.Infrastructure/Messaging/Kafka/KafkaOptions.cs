using System.ComponentModel.DataAnnotations;

namespace DocumentIntelligence.Infrastructure.Messaging.Kafka;

public sealed class KafkaOptions : IValidatableObject
{
    public const string SectionName = "Kafka";

    /// <summary>Comma-separated host:port list, for example "kafka:9092".</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    public string ClientId { get; set; } = "document-intelligence";

    public KafkaTopics Topics { get; set; } = new();

    /// <summary>Consumer group of the ingestion workers; partitions are shared among its members.</summary>
    public string IngestionConsumerGroup { get; set; } = "ingestion-worker";

    /// <summary>
    /// Prefix of the per-instance consumer groups API replicas use to follow index changes. Every
    /// replica needs every change, so each one joins its own group.
    /// </summary>
    public string IndexSyncConsumerGroupPrefix { get; set; } = "api-index-sync";

    /// <summary>Create missing topics at start-up (convenient locally; usually off where topics are managed).</summary>
    public bool CreateTopics { get; set; } = true;

    [Range(1, 1000)]
    public int TopicPartitions { get; set; } = 3;

    [Range(1, 10)]
    public short TopicReplicationFactor { get; set; } = 1;

    /// <summary>Optional SASL settings for managed clusters (for example Azure Event Hubs or Confluent Cloud).</summary>
    public string? SecurityProtocol { get; set; }

    public string? SaslMechanism { get; set; }

    public string? SaslUsername { get; set; }

    public string? SaslPassword { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(BootstrapServers))
        {
            yield return new ValidationResult($"{nameof(BootstrapServers)} is required.", [nameof(BootstrapServers)]);
        }

        var topics = new[] { Topics.DocumentUploaded, Topics.DeadLetter, Topics.IndexChanged };
        if (topics.Any(string.IsNullOrWhiteSpace) || topics.Distinct(StringComparer.Ordinal).Count() != topics.Length)
        {
            yield return new ValidationResult("The three topic names must be set and distinct.", [nameof(Topics)]);
        }
    }
}

public sealed class KafkaTopics
{
    public string DocumentUploaded { get; set; } = "document-uploaded";

    public string DeadLetter { get; set; } = "document-uploaded-dlq";

    public string IndexChanged { get; set; } = "document-index-changed";
}
