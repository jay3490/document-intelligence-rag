namespace DocumentIntelligence.Api.Ingestion;

public static class IngestionServiceCollectionExtensions
{
    /// <summary>Registers the upload queue, job tracking and the two background services.</summary>
    public static IServiceCollection AddBackgroundIngestion(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IngestionQueue>();
        services.AddSingleton<KeywordIndexInitializer>();
        services.AddHostedService(sp => sp.GetRequiredService<KeywordIndexInitializer>());
        services.AddHostedService<IngestionWorker>();
        return services;
    }
}
