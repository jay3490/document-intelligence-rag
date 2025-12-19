using DocumentIntelligence.Core.Ingestion;
using DocumentIntelligence.Core.Messaging;
using DocumentIntelligence.Core.Search;
using DocumentIntelligence.Infrastructure.Messaging.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DocumentIntelligence.Infrastructure.Messaging;

/// <summary>What a host does with ingestion messages.</summary>
public enum IngestionHostRole
{
    /// <summary>
    /// Publishes uploads. With the in-process transport it also ingests them; with Kafka it
    /// follows index changes to keep its keyword index current.
    /// </summary>
    Api,

    /// <summary>Consumes uploads from Kafka and ingests them.</summary>
    Worker,

    /// <summary>Only publishes uploads (the blob-trigger function).</summary>
    Publisher,
}

public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddIngestionMessaging(this IServiceCollection services, IConfiguration configuration, IngestionHostRole role)
    {
        services.TryAddSingleton(TimeProvider.System);
        var transport = configuration.GetSection(IngestionOptions.SectionName).GetValue<IngestionTransport?>(nameof(IngestionOptions.Transport))
            ?? IngestionTransport.InProcess;

        if (role != IngestionHostRole.Publisher)
        {
            services.AddSingleton<DocumentUploadedHandler>();
        }

        switch (transport)
        {
            case IngestionTransport.InProcess when role == IngestionHostRole.Api:
                services.AddSingleton<InProcessIngestionChannel>();
                services.AddSingleton<IIngestionEventPublisher, InProcessIngestionEventPublisher>();
                services.AddHostedService<InProcessIngestionConsumer>();
                break;

            case IngestionTransport.InProcess:
                throw new InvalidOperationException(
                    $"The {role} host needs {IngestionOptions.SectionName}:{nameof(IngestionOptions.Transport)} = {IngestionTransport.Kafka}.");

            case IngestionTransport.Kafka:
                services.AddOptions<KafkaOptions>()
                    .Bind(configuration.GetSection(KafkaOptions.SectionName))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                services.AddSingleton<IMessageProducer, ConfluentMessageProducer>();
                services.AddSingleton<IIngestionEventPublisher, KafkaIngestionEventPublisher>();
                services.AddHostedService<KafkaTopicInitializer>();

                if (role == IngestionHostRole.Api)
                {
                    services.AddSingleton<KeywordIndexSynchronizer>();
                    services.AddHostedService<IndexChangedKafkaConsumer>();
                }
                else if (role == IngestionHostRole.Worker)
                {
                    services.AddHostedService<DocumentUploadedKafkaConsumer>();
                }

                break;

            default:
                throw new InvalidOperationException($"Unsupported ingestion transport '{transport}'.");
        }

        return services;
    }
}
