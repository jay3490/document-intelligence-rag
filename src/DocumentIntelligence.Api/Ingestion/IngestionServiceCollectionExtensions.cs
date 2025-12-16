using DocumentIntelligence.Core.Ingestion;
using DocumentIntelligence.Infrastructure.Messaging;

namespace DocumentIntelligence.Api.Ingestion;

public static class IngestionServiceCollectionExtensions
{
    /// <summary>
    /// Registers the keyword index rebuild and the ingestion transport. With the in-process
    /// transport the API also consumes uploads itself; with Kafka the ingestion worker does.
    /// </summary>
    public static IServiceCollection AddBackgroundIngestion(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<KeywordIndexInitializer>();
        services.AddSingleton<IIngestionReadiness>(sp => sp.GetRequiredService<KeywordIndexInitializer>());
        services.AddHostedService(sp => sp.GetRequiredService<KeywordIndexInitializer>());
        services.AddIngestionMessaging(configuration, consumeInProcess: true);
        return services;
    }
}
