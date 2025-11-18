using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocumentIntelligence.Api.Ingestion;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DocumentIntelligence.Api.Tests;

/// <summary>Hosts the API in memory with the default offline providers and in-memory store.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
    }
}

internal static class ApiClientExtensions
{
    public static Task<HttpResponseMessage> UploadAsync(this HttpClient client, string fileName, string content) =>
        client.UploadAsync(fileName, Encoding.UTF8.GetBytes(content));

    public static async Task<HttpResponseMessage> UploadAsync(this HttpClient client, string fileName, byte[] content)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return await client.PostAsync("/api/documents", form);
    }

    /// <summary>Uploads a document and polls its job until the worker has finished with it.</summary>
    public static async Task<IngestionJob> IngestAsync(this HttpClient client, string fileName, string content)
    {
        using var response = await client.UploadAsync(fileName, content);
        response.EnsureSuccessStatusCode();
        var job = (await response.Content.ReadFromJsonAsync<IngestionJob>(ApiFactory.Json))!;

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (job.Status is IngestionStatus.Queued or IngestionStatus.Processing)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Ingestion job {job.Id} did not finish.");
            }

            await Task.Delay(25);
            job = (await client.GetFromJsonAsync<IngestionJob>($"/api/ingestions/{job.Id}", ApiFactory.Json))!;
        }

        return job;
    }
}
