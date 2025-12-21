using DocumentIntelligence.Core.Ingestion;
using DocumentIntelligence.Functions.Drops;
using DocumentIntelligence.Infrastructure;
using DocumentIntelligence.Infrastructure.Messaging;
using DocumentIntelligence.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentIntelligence.Functions;

public static class FunctionServices
{
    /// <summary>
    /// The blob store, the upload publisher (Kafka) and read access to the job database. The
    /// functions never ingest or migrate anything themselves.
    /// </summary>
    public static IServiceCollection AddDropFunctions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IngestionOptions>()
            .Bind(configuration.GetSection(IngestionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddBlobStorage(configuration);
        services.AddIngestionMessaging(configuration, IngestionHostRole.Publisher);
        services.AddIngestionJobPersistence(configuration);
        services.AddSingleton<BlobDropHandler>();
        services.AddSingleton<IngestionStatusQuery>();
        return services;
    }
}
