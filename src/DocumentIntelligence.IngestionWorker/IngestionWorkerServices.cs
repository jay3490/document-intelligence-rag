using DocumentIntelligence.Core.Search;
using DocumentIntelligence.Infrastructure;
using DocumentIntelligence.Infrastructure.Messaging;
using DocumentIntelligence.Infrastructure.Messaging.Kafka;
using DocumentIntelligence.Infrastructure.Persistence;
using DocumentIntelligence.Infrastructure.VectorStores;

namespace DocumentIntelligence.IngestionWorker;

public static class IngestionWorkerServices
{
    public const string ReadyTag = "ready";

    /// <summary>
    /// Everything the worker runs: the ingestion pipeline, the job database, the Kafka upload
    /// consumer and readiness checks for the vector store, database and Kafka.
    /// </summary>
    public static IServiceCollection AddIngestionWorker(this IServiceCollection services, IConfiguration configuration)
    {
        // The worker answers no queries, so it keeps no keyword index; API replicas update
        // theirs from the index-change topic.
        services.AddSingleton<IKeywordIndexWriter, NoKeywordIndexWriter>();
        services.AddDocumentIntelligence(configuration);
        services.AddIngestionJobPersistence(configuration);
        services.AddIngestionMessaging(configuration, IngestionHostRole.Worker);

        services.AddHealthChecks()
            .AddCheck<VectorStoreHealthCheck>("vector-store", tags: [ReadyTag])
            .AddDbContextCheck<IngestionDbContext>("job-database", tags: [ReadyTag])
            .AddCheck<KafkaHealthCheck>("kafka", tags: [ReadyTag]);

        return services;
    }
}
