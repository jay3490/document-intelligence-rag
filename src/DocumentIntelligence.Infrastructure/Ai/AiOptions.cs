using System.ComponentModel.DataAnnotations;

namespace DocumentIntelligence.Infrastructure.Ai;

public enum AiProvider
{
    /// <summary>Deterministic local stand-ins: hashed embeddings and an extractive answerer.</summary>
    Offline,
}

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public AiProvider Provider { get; set; } = AiProvider.Offline;

    /// <summary>
    /// Length of every embedding vector. The vector store is created with this size, so it
    /// must match what the embedding model returns.
    /// </summary>
    [Range(8, 8192)]
    public int EmbeddingDimensions { get; set; } = 512;
}
