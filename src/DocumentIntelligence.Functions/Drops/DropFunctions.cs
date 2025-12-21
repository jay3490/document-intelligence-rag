using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace DocumentIntelligence.Functions.Drops;

/// <summary>
/// Entry points bound by the Functions host. They only translate bindings to and from the
/// handler and query classes, which hold the logic and are tested on their own.
/// </summary>
public sealed class DropFunctions(BlobDropHandler dropHandler, IngestionStatusQuery statusQuery)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Fires for every file written to the drop container (app setting <c>DropContainer</c>,
    /// storage connection <c>DropStorage</c>) and publishes it for ingestion.
    /// </summary>
    [Function("IngestDroppedBlob")]
    public async Task IngestDroppedBlobAsync(
        [BlobTrigger("%DropContainer%/{path}", Connection = "DropStorage")] byte[] content,
        string path,
        FunctionContext context)
    {
        var drop = new BlobDrop(path, content, BlobTriggerMetadata.Read(context.BindingContext.BindingData));
        await dropHandler.HandleAsync(drop, correlationId: context.InvocationId, context.CancellationToken);
    }

    /// <summary>
    /// <c>GET /api/tenants/{tenant}/ingestions</c> lists recent jobs (optional <c>?limit=</c>);
    /// <c>GET /api/tenants/{tenant}/ingestions/{jobId}</c> returns one job.
    /// </summary>
    [Function("IngestionStatus")]
    public async Task<HttpResponseData> IngestionStatusAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "tenants/{tenant}/ingestions/{jobId:guid?}")] HttpRequestData request,
        string tenant,
        Guid? jobId,
        FunctionContext context)
    {
        var cancellationToken = context.CancellationToken;

        if (jobId is { } id)
        {
            var (result, job) = await statusQuery.GetAsync(tenant, id, cancellationToken);
            return await RespondAsync(request, result, job);
        }

        var limitText = System.Web.HttpUtility.ParseQueryString(request.Url.Query)["limit"];
        int? limit = int.TryParse(limitText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        var (listResult, jobs) = await statusQuery.ListAsync(tenant, limit, cancellationToken);
        return await RespondAsync(request, listResult, jobs);
    }

    private static async Task<HttpResponseData> RespondAsync(HttpRequestData request, StatusQueryResult result, object? body)
    {
        var (status, payload) = result switch
        {
            StatusQueryResult.Found => (HttpStatusCode.OK, body),
            StatusQueryResult.InvalidTenant => (HttpStatusCode.BadRequest, new { title = "Invalid tenant id" }),
            _ => (HttpStatusCode.NotFound, (object?)new { title = "Ingestion job not found" }),
        };

        var response = request.CreateResponse(status);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(payload, Json));
        return response;
    }
}

/// <summary>Reads the blob's metadata from the trigger's binding data, where it arrives as JSON.</summary>
public static class BlobTriggerMetadata
{
    public static IReadOnlyDictionary<string, string> Read(IReadOnlyDictionary<string, object?> bindingData)
    {
        if (!bindingData.TryGetValue("Metadata", out var raw) || raw is null)
        {
            return new Dictionary<string, string>();
        }

        try
        {
            return raw as IReadOnlyDictionary<string, string>
                ?? JsonSerializer.Deserialize<Dictionary<string, string>>(raw.ToString()!)
                ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}
