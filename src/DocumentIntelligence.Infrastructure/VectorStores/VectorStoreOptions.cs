using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace DocumentIntelligence.Infrastructure.VectorStores;

public enum VectorStoreProvider
{
    /// <summary>Process-local storage; nothing survives a restart.</summary>
    InMemory,

    /// <summary>A Qdrant server, reached over its REST API.</summary>
    Qdrant,
}

public sealed partial class VectorStoreOptions : IValidatableObject
{
    public const string SectionName = "VectorStore";

    public VectorStoreProvider Provider { get; set; } = VectorStoreProvider.InMemory;

    public QdrantOptions Qdrant { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Provider != VectorStoreProvider.Qdrant)
        {
            yield break;
        }

        if (!Uri.TryCreate(Qdrant.Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            yield return new ValidationResult("Qdrant:Url must be an absolute http or https URL.", ["Qdrant.Url"]);
        }

        if (!CollectionNamePattern().IsMatch(Qdrant.Collection))
        {
            yield return new ValidationResult(
                "Qdrant:Collection may only contain letters, digits, '-' and '_'.", ["Qdrant.Collection"]);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,255}$")]
    private static partial Regex CollectionNamePattern();
}

public sealed class QdrantOptions
{
    /// <summary>The REST endpoint, port 6333 by default.</summary>
    public string Url { get; set; } = "http://localhost:6333";

    /// <summary>Sent in the <c>api-key</c> header when the server requires one.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The collection to use. It is created on first use with the configured embedding size;
    /// switching to a model with a different size needs a new collection name.
    /// </summary>
    public string Collection { get; set; } = "documents";
}
