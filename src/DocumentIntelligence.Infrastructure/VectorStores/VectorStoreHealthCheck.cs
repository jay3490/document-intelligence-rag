using DocumentIntelligence.Infrastructure.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Infrastructure.VectorStores;

/// <summary>Reports whether the configured vector store can serve requests.</summary>
public sealed class VectorStoreHealthCheck(IHttpClientFactory httpClientFactory, IOptions<VectorStoreOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (options.Value.Provider == VectorStoreProvider.InMemory)
        {
            return HealthCheckResult.Healthy("In-memory vector store.");
        }

        try
        {
            var client = httpClientFactory.CreateClient(QdrantVectorStore.HttpClientName);
            using var response = await JsonHttp.SendRawAsync(client, HttpMethod.Get, "readyz", null, "qdrant", cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Qdrant is ready.")
                : HealthCheckResult.Unhealthy($"Qdrant readiness returned {(int)response.StatusCode}.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Qdrant is unreachable.", exception);
        }
    }
}
