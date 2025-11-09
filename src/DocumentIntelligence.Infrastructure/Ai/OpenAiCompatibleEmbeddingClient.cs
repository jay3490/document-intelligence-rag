using DocumentIntelligence.Core;
using DocumentIntelligence.Core.Embeddings;
using DocumentIntelligence.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Infrastructure.Ai;

/// <summary>Calls <c>POST {BaseUrl}/embeddings</c> on an OpenAI-compatible server.</summary>
public sealed class OpenAiCompatibleEmbeddingClient(IHttpClientFactory httpClientFactory, IOptions<AiOptions> options) : IEmbeddingClient
{
    private const string Service = "embeddings";

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count == 0)
        {
            return [];
        }

        var ai = options.Value;
        var client = httpClientFactory.CreateClient(OpenAiHttpClient.Name);
        var response = await JsonHttp.SendAsync<EmbeddingResponse>(
            client, HttpMethod.Post, "embeddings", new EmbeddingRequest(ai.EmbeddingModel, texts), Service, cancellationToken);

        var vectors = response.Data.OrderBy(item => item.Index).Select(item => item.Embedding).ToList();
        if (vectors.Count != texts.Count)
        {
            throw new UpstreamServiceException(Service, $"Expected {texts.Count} embeddings but received {vectors.Count}.");
        }

        if (vectors.FirstOrDefault(v => v.Length != ai.EmbeddingDimensions) is { } mismatched)
        {
            throw new UpstreamServiceException(
                Service,
                $"Model '{ai.EmbeddingModel}' returned {mismatched.Length}-dimensional vectors, but " +
                $"{AiOptions.SectionName}:{nameof(AiOptions.EmbeddingDimensions)} is {ai.EmbeddingDimensions}.");
        }

        return vectors;
    }

    private sealed record EmbeddingRequest(string Model, IReadOnlyList<string> Input);

    private sealed record EmbeddingResponse(IReadOnlyList<EmbeddingItem> Data);

    private sealed record EmbeddingItem(int Index, float[] Embedding);
}
