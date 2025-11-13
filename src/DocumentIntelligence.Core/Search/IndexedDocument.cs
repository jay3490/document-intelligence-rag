namespace DocumentIntelligence.Core.Search;

/// <summary>A document as currently represented in the search index.</summary>
public sealed record IndexedDocument(Guid Id, string Name, int ChunkCount, int TokenCount);
