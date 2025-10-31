namespace DocumentIntelligence.Core.Embeddings;

/// <summary>Turns text into dense vectors. Implementations must return one vector per input, in order.</summary>
public interface IEmbeddingClient
{
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}
