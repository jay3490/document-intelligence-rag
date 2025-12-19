using Confluent.Kafka;
using DocumentIntelligence.Core;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Infrastructure.Messaging.Kafka;

/// <summary>
/// An idempotent Confluent producer (acks=all), so a retried send never duplicates a record
/// within a partition. Delivery failures surface as <see cref="UpstreamServiceException"/>.
/// </summary>
public sealed class ConfluentMessageProducer : IMessageProducer, IDisposable
{
    private const string Service = "kafka";

    private readonly IProducer<string, byte[]> _producer;

    public ConfluentMessageProducer(IOptions<KafkaOptions> options)
    {
        var config = new ProducerConfig(KafkaClientConfig.Build(options.Value))
        {
            EnableIdempotence = true,
            Acks = Acks.All,
            LingerMs = 5,
            MessageTimeoutMs = 30_000,
        };

        _producer = new ProducerBuilder<string, byte[]>(config).Build();
    }

    public async Task ProduceAsync(string topic, KafkaRecord record, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(record);

        try
        {
            await _producer.ProduceAsync(
                topic,
                new Message<string, byte[]> { Key = record.Key, Value = record.Value, Headers = KafkaSerializer.WriteHeaders(record.Headers) },
                cancellationToken);
        }
        catch (KafkaException exception)
        {
            throw new UpstreamServiceException(Service, $"Publishing to '{topic}' failed: {exception.Error.Reason}", exception);
        }
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}

internal static class KafkaClientConfig
{
    /// <summary>Connection and security settings shared by producers, consumers and the admin client.</summary>
    public static ClientConfig Build(KafkaOptions options)
    {
        var config = new ClientConfig
        {
            BootstrapServers = options.BootstrapServers,
            ClientId = options.ClientId,
        };

        if (Enum.TryParse<SecurityProtocol>(options.SecurityProtocol, ignoreCase: true, out var protocol))
        {
            config.SecurityProtocol = protocol;
        }

        if (Enum.TryParse<SaslMechanism>(options.SaslMechanism, ignoreCase: true, out var mechanism))
        {
            config.SaslMechanism = mechanism;
            config.SaslUsername = options.SaslUsername;
            config.SaslPassword = options.SaslPassword;
        }

        return config;
    }
}
