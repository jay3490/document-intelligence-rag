using System.Text.Json;
using DocumentIntelligence.Api.Ingestion;
using DocumentIntelligence.Infrastructure.Persistence;
using DocumentIntelligence.Infrastructure.VectorStores;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DocumentIntelligence.Api.Hosting;

public static class HealthEndpoints
{
    private const string ReadyTag = "ready";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IServiceCollection AddServiceHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<VectorStoreHealthCheck>("vector-store", tags: [ReadyTag])
            .AddCheck<KeywordIndexHealthCheck>("keyword-index", tags: [ReadyTag])
            .AddDbContextCheck<IngestionDbContext>("job-database", tags: [ReadyTag]);
        return services;
    }

    /// <summary>
    /// <c>/health/live</c> only shows the process is serving requests; <c>/health/ready</c>
    /// also checks the vector store, the job database and that the keyword index has been rebuilt.
    /// </summary>
    public static IEndpointRouteBuilder MapServiceHealthChecks(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteJsonAsync,
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WriteJsonAsync,
        });

        return app;
    }

    private static Task WriteJsonAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var body = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
            }),
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions));
    }
}

internal sealed class KeywordIndexHealthCheck(KeywordIndexInitializer initializer) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(initializer.IsReady
            ? HealthCheckResult.Healthy("Keyword index is loaded.")
            : HealthCheckResult.Unhealthy("Keyword index is still being rebuilt from the vector store."));
}
