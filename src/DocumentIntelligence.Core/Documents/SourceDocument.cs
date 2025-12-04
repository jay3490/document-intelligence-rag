using DocumentIntelligence.Core.Tenancy;

namespace DocumentIntelligence.Core.Documents;

/// <summary>A document submitted for ingestion, before it is split into chunks.</summary>
public sealed record SourceDocument(Guid Id, string Name, DocumentFormat Format, string Content)
{
    public string TenantId { get; init; } = Tenancy.TenantId.Default;

    public DocumentMetadata Metadata { get; init; } = DocumentMetadata.None;
}
