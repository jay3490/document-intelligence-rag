using System.ComponentModel.DataAnnotations;

namespace DocumentIntelligence.Core.Search;

public sealed class RetrievalOptions : IValidatableObject
{
    public const string SectionName = "Retrieval";

    /// <summary>Chunks handed to the answer generator when the request does not say otherwise.</summary>
    [Range(1, 50)]
    public int DefaultTopK { get; set; } = 5;

    /// <summary>Candidates fetched from each retriever (vector and keyword) before fusion.</summary>
    [Range(1, 500)]
    public int CandidatesPerRetriever { get; set; } = 30;

    /// <summary>The RRF smoothing constant; larger values flatten the advantage of top ranks.</summary>
    [Range(1, 1000)]
    public int RrfK { get; set; } = ReciprocalRankFusion.DefaultK;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CandidatesPerRetriever < DefaultTopK)
        {
            yield return new ValidationResult(
                $"{nameof(CandidatesPerRetriever)} must be at least {nameof(DefaultTopK)}.",
                [nameof(CandidatesPerRetriever)]);
        }
    }
}
