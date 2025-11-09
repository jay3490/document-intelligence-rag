using System.Net.Http.Headers;

namespace DocumentIntelligence.Infrastructure.Ai;

/// <summary>Configuration of the named <see cref="HttpClient"/> used for OpenAI-compatible calls.</summary>
internal static class OpenAiHttpClient
{
    public const string Name = "openai-compatible";

    public static void Configure(HttpClient client, AiOptions options)
    {
        // A trailing slash makes relative paths such as "embeddings" append to "/v1/".
        client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }
    }
}
