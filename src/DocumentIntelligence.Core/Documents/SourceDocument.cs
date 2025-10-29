namespace DocumentIntelligence.Core.Documents;

/// <summary>A document submitted for ingestion, before it is split into chunks.</summary>
public sealed record SourceDocument(Guid Id, string Name, DocumentFormat Format, string Content);
