using System.ComponentModel.DataAnnotations;

namespace DocumentIntelligence.Core.Ingestion;

public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    /// <summary>Chunks sent to the embedding endpoint (and written to the store) per request.</summary>
    [Range(1, 2048)]
    public int EmbeddingBatchSize { get; set; } = 32;

    /// <summary>Largest accepted upload, in bytes.</summary>
    [Range(1, 100 * 1024 * 1024)]
    public int MaxDocumentBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Uploads that may wait for the background worker before new ones are refused.</summary>
    [Range(1, 10_000)]
    public int QueueCapacity { get; set; } = 100;
}
