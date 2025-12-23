using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocumentIntelligence.Api.Security;
using DocumentIntelligence.Core.Ingestion;
using Microsoft.Extensions.DependencyInjection;
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

    private readonly string _blobRoot = Path.Combine(Path.GetTempPath(), "di-api-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");

        // Every factory gets its own in-memory job database and blob directory.
        builder.UseSetting("Persistence:ConnectionString", $"Data Source=api-tests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        builder.UseSetting("BlobStorage:FileSystem:RootPath", _blobRoot);

        builder.UseSetting("Auth:Mode", "DevToken");
        builder.UseSetting("Auth:DevToken:SigningKey", SigningKey);
    }

    public const string SigningKey = "api-tests-signing-key-0123456789-abcdef";

    public const string DefaultTenant = "default";

    /// <summary>A development token for a tenant, signed with the test key.</summary>
    public string TokenFor(string tenant) => Services.GetRequiredService<DevTokenIssuer>().Issue(tenant, "tester").AccessToken;

    /// <summary>A client that calls the API as <paramref name="tenant"/>.</summary>
    public HttpClient CreateClientFor(string tenant)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor(tenant));
        return client;
    }

    /// <summary>Clients from <c>CreateClient()</c> act for the default tenant unless told otherwise.</summary>
    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor(DefaultTenant));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_blobRoot))
        {
            Directory.Delete(_blobRoot, recursive: true);
        }
    }
}

internal static class ApiClientExtensions
{
    public static Task<HttpResponseMessage> UploadAsync(this HttpClient client, string fileName, string content, Dictionary<string, string>? fields = null) =>
        client.UploadAsync(fileName, Encoding.UTF8.GetBytes(content), fields);

    public static async Task<HttpResponseMessage> UploadAsync(this HttpClient client, string fileName, byte[] content, Dictionary<string, string>? fields = null)
    {
        using var form = Form(fileName, content, fields);
        return await client.PostAsync("/api/documents", form);
    }

    public static MultipartFormDataContent Form(string fileName, string content, Dictionary<string, string>? fields = null) =>
        Form(fileName, Encoding.UTF8.GetBytes(content), fields);

    public static MultipartFormDataContent Form(string fileName, byte[] content, Dictionary<string, string>? fields = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        foreach (var (name, value) in fields ?? [])
        {
            form.Add(new StringContent(value), name);
        }

        return form;
    }

    /// <summary>Uploads a document and polls its job until the worker has finished with it.</summary>
    public static async Task<IngestionJob> IngestAsync(this HttpClient client, string fileName, string content, Dictionary<string, string>? fields = null)
    {
        using var response = await client.UploadAsync(fileName, content, fields);
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
