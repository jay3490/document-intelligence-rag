using System.ComponentModel.DataAnnotations;

namespace DocumentIntelligence.Core.Ingestion;

public enum IngestionTransport
{
    /// <summary>A bounded in-memory channel inside the API process; for local development and tests.</summary>
    InProcess,

    /// <summary>Kafka topics consumed by the separate ingestion worker.</summary>
    Kafka,
}

public sealed class IngestionOptions : IValidatableObject
{
    public const string SectionName = "Ingestion";

    public IngestionTransport Transport { get; set; } = IngestionTransport.InProcess;

    /// <summary>Chunks sent to the embedding endpoint (and written to the store) per request.</summary>
    [Range(1, 2048)]
    public int EmbeddingBatchSize { get; set; } = 32;

    /// <summary>Largest accepted upload, in bytes. Capped below Kestrel's default 30 MB body limit.</summary>
    [Range(1, 25 * 1024 * 1024)]
    public int MaxDocumentBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Uploads that may wait in the in-process channel before new ones are refused.</summary>
    [Range(1, 10_000)]
    public int QueueCapacity { get; set; } = 100;

    /// <summary>Attempts per upload, including the first, before it is dead-lettered.</summary>
    [Range(1, 20)]
    public int MaxAttempts { get; set; } = 4;

    /// <summary>Delay before the first retry; each further retry doubles it, up to the maximum.</summary>
    [Range(0, 60_000)]
    public int RetryBaseDelayMilliseconds { get; set; } = 1000;

    [Range(0, 600_000)]
    public int RetryMaxDelayMilliseconds { get; set; } = 30_000;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RetryMaxDelayMilliseconds < RetryBaseDelayMilliseconds)
        {
            yield return new ValidationResult(
                $"{nameof(RetryMaxDelayMilliseconds)} must be at least {nameof(RetryBaseDelayMilliseconds)}.",
                [nameof(RetryMaxDelayMilliseconds)]);
        }
    }

    /// <summary>The wait before attempt <paramref name="nextAttempt"/> (2 or later).</summary>
    public TimeSpan RetryDelay(int nextAttempt)
    {
        var exponent = Math.Clamp(nextAttempt - 2, 0, 20);
        var delay = Math.Min((double)RetryBaseDelayMilliseconds * (1 << exponent), RetryMaxDelayMilliseconds);
        return TimeSpan.FromMilliseconds(delay);
    }
}
