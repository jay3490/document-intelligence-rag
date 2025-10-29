using System.ComponentModel.DataAnnotations;

namespace DocumentIntelligence.Core.Chunking;

public sealed class ChunkingOptions : IValidatableObject
{
    public const string SectionName = "Chunking";

    /// <summary>Upper bound on the tokens in a single chunk.</summary>
    [Range(16, 8192)]
    public int MaxTokens { get; set; } = 256;

    /// <summary>
    /// How many tokens from the end of one chunk are repeated at the start of the next.
    /// Overlap is made of whole sentences, so the actual overlap can be smaller.
    /// </summary>
    [Range(0, 4096)]
    public int OverlapTokens { get; set; } = 40;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (OverlapTokens >= MaxTokens)
        {
            yield return new ValidationResult(
                $"{nameof(OverlapTokens)} must be smaller than {nameof(MaxTokens)}.",
                [nameof(OverlapTokens)]);
        }
    }
}
