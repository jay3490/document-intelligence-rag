using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Infrastructure.Messaging.Kafka;

/// <summary>
/// A Kafka consume loop. Offsets are stored only after a record was handled or parked, and
/// committed in the background, so delivery is at least once. A record whose handler fails is
/// sought back to and retried after a pause instead of being skipped.
/// </summary>
public abstract partial class KafkaConsumerService<TMessage> : BackgroundService
{
    private static readonly TimeSpan FailurePause = TimeSpan.FromSeconds(5);

    private readonly KafkaOptions _options;
    private readonly IMessageProducer _producer;
    private readonly ILogger _logger;

    protected KafkaConsumerService(IOptions<KafkaOptions> options, IMessageProducer producer, ILogger logger)
    {
        _options = options.Value;
        _producer = producer;
        _logger = logger;
    }

    protected KafkaOptions Options => _options;

    protected abstract string Topic { get; }

    protected abstract string GroupId { get; }

    /// <summary>Where a consumer group without committed offsets starts reading.</summary>
    protected virtual AutoOffsetReset OffsetReset => AutoOffsetReset.Earliest;

    protected abstract Task HandleAsync(TMessage message, CancellationToken cancellationToken);

    /// <summary>Runs before the first record is consumed, for example to wait for dependencies.</summary>
    protected virtual Task BeforeConsumingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        // Consume() blocks, so the loop gets its own thread instead of a pool thread.
        Task.Factory.StartNew(() => RunAsync(stoppingToken), stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        await BeforeConsumingAsync(stoppingToken);

        var config = new ConsumerConfig(KafkaClientConfig.Build(_options))
        {
            GroupId = GroupId,
            AutoOffsetReset = OffsetReset,
            EnableAutoCommit = true,
            EnableAutoOffsetStore = false,
        };

        var dispatcher = new KafkaRecordDispatcher<TMessage>(HandleAsync, _producer, _options.Topics.DeadLetter, _logger);
        using var consumer = new ConsumerBuilder<string, byte[]>(config)
            .SetErrorHandler((_, error) => LogConsumerError(_logger, Topic, error.Reason))
            .Build();

        consumer.Subscribe(Topic);
        LogStarted(_logger, Topic, GroupId);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = consumer.Consume(stoppingToken);
                if (result?.Message is null)
                {
                    continue;
                }

                var record = new KafkaRecord(result.Message.Key ?? string.Empty, result.Message.Value ?? [], KafkaSerializer.ReadHeaders(result.Message.Headers));
                var outcome = await dispatcher.DispatchAsync(result.Topic, record, stoppingToken);

                if (outcome == DispatchResult.Failed)
                {
                    consumer.Seek(result.TopicPartitionOffset);
                    await Task.Delay(FailurePause, stoppingToken);
                    continue;
                }

                consumer.StoreOffset(result);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        finally
        {
            consumer.Close();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Consuming {Topic} as group {GroupId}")]
    private static partial void LogStarted(ILogger logger, string topic, string groupId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kafka consumer error on {Topic}: {Reason}")]
    private static partial void LogConsumerError(ILogger logger, string topic, string reason);
}
