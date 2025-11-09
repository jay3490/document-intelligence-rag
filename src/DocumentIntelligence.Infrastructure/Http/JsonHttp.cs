using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocumentIntelligence.Core;

namespace DocumentIntelligence.Infrastructure.Http;

/// <summary>
/// JSON-over-HTTP helpers shared by the OpenAI-compatible and Qdrant clients. Transport
/// failures, timeouts, error statuses and unreadable bodies all surface as
/// <see cref="UpstreamServiceException"/> naming the dependency.
/// </summary>
internal static class JsonHttp
{
    private const int MaxErrorBodyLength = 500;

    public static readonly JsonSerializerOptions SnakeCase = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task<TResponse> SendAsync<TResponse>(
        HttpClient client,
        HttpMethod method,
        string path,
        object? body,
        string service,
        CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(client, method, path, body, service, cancellationToken);
        await EnsureSuccessAsync(response, service, cancellationToken);

        try
        {
            return await response.Content.ReadFromJsonAsync<TResponse>(SnakeCase, cancellationToken)
                ?? throw new UpstreamServiceException(service, $"{service} returned an empty response body.");
        }
        catch (JsonException exception)
        {
            throw new UpstreamServiceException(service, $"{service} returned a response that could not be parsed.", exception);
        }
    }

    public static async Task<HttpResponseMessage> SendRawAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object? body,
        string service,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: SnakeCase);
        }

        try
        {
            return await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new UpstreamServiceException(service, $"Could not reach {service}: {exception.Message}", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpstreamServiceException(service, $"The request to {service} timed out.", exception);
        }
    }

    public static async Task EnsureSuccessAsync(HttpResponseMessage response, string service, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length > MaxErrorBodyLength)
        {
            body = body[..MaxErrorBodyLength] + "...";
        }

        throw new UpstreamServiceException(
            service,
            $"{service} returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }
}
