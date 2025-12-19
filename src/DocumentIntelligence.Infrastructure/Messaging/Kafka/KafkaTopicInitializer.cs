using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Infrastructure.Messaging.Kafka;

/// <summary>Creates the ingestion topics when <see cref="KafkaOptions.CreateTopics"/> is on.</summary>
public sealed partial class KafkaTopicInitializer(IOptions<KafkaOptions> options, ILogger<KafkaTopicInitializer> logger) : IHostedService
{
    private const int MaxAttempts = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.CreateTopics)
        {
            return;
        }

        var topics = new[] { settings.Topics.DocumentUploaded, settings.Topics.DeadLetter, settings.Topics.IndexChanged }
            .Select(name => new TopicSpecification
            {
                Name = name,
                NumPartitions = settings.TopicPartitions,
                ReplicationFactor = settings.TopicReplicationFactor,
            })
            .ToList();

        using var admin = new AdminClientBuilder(new AdminClientConfig(KafkaClientConfig.Build(settings))).Build();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await admin.CreateTopicsAsync(topics);
                LogCreated(logger, string.Join(", ", topics.Select(t => t.Name)));
                return;
            }
            catch (CreateTopicsException exception) when (exception.Results.All(r => r.Error.Code is ErrorCode.NoError or ErrorCode.TopicAlreadyExists))
            {
                return;
            }
            catch (KafkaException exception) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                LogRetrying(logger, exception, attempt, RetryDelay.TotalSeconds);
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Created Kafka topics {Topics}")]
    private static partial void LogCreated(ILogger logger, string topics);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kafka is not reachable yet (attempt {Attempt}); retrying in {DelaySeconds}s")]
    private static partial void LogRetrying(ILogger logger, Exception exception, int attempt, double delaySeconds);
}
