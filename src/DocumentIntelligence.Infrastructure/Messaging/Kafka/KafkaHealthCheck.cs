using Confluent.Kafka;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Infrastructure.Messaging.Kafka;

/// <summary>Healthy when the cluster answers a metadata request for the upload topic.</summary>
public sealed class KafkaHealthCheck(IOptions<KafkaOptions> options) : IHealthCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        try
        {
            using var admin = new AdminClientBuilder(new AdminClientConfig(KafkaClientConfig.Build(settings))).Build();
            var metadata = admin.GetMetadata(settings.Topics.DocumentUploaded, Timeout);
            return Task.FromResult(metadata.Brokers.Count > 0
                ? HealthCheckResult.Healthy($"{metadata.Brokers.Count} broker(s) reachable.")
                : HealthCheckResult.Unhealthy("No Kafka brokers are reachable."));
        }
        catch (KafkaException exception)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Kafka is unreachable.", exception));
        }
    }
}
