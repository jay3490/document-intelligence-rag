using System.ComponentModel.DataAnnotations;

namespace DocumentIntelligence.Infrastructure.Ai;

public enum AiProvider
{
    /// <summary>Deterministic local stand-ins: hashed embeddings and an extractive answerer.</summary>
    Offline,

    /// <summary>
    /// Any server that implements the OpenAI <c>/embeddings</c> and <c>/chat/completions</c>
    /// endpoints: OpenAI itself, Ollama (<c>http://localhost:11434/v1</c>), vLLM and others.
    /// </summary>
    OpenAICompatible,
}

public sealed class AiOptions : IValidatableObject
{
    public const string SectionName = "Ai";

    public AiProvider Provider { get; set; } = AiProvider.Offline;

    /// <summary>Base URL of the OpenAI-compatible API, including the version segment.</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>Sent as a bearer token when set. Local servers such as Ollama need none.</summary>
    public string? ApiKey { get; set; }

    [Required]
    public string ChatModel { get; set; } = "gpt-4o-mini";

    [Required]
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";

    /// <summary>
    /// Length of every embedding vector. The vector store is created with this size, so it
    /// must match what the embedding model returns.
    /// </summary>
    [Range(8, 8192)]
    public int EmbeddingDimensions { get; set; } = 512;

    [Range(0.0, 2.0)]
    public double Temperature { get; set; } = 0.1;

    [Range(1, 600)]
    public int RequestTimeoutSeconds { get; set; } = 120;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Provider == AiProvider.OpenAICompatible &&
            !(Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
        {
            yield return new ValidationResult($"{nameof(BaseUrl)} must be an absolute http or https URL.", [nameof(BaseUrl)]);
        }
    }
}
