using DocumentIntelligence.Core.Documents;

namespace DocumentIntelligence.Core.Search;

/// <summary>A chunk returned by a single retriever, with that retriever's raw score.</summary>
public sealed record ScoredChunk(DocumentChunk Chunk, double Score);
