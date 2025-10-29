namespace DocumentIntelligence.Core.Chunking;

/// <summary>A chunk produced by <see cref="TextChunker"/>, before it is bound to a document.</summary>
public sealed record TextChunk(int Index, string? Section, string Text, int TokenCount);
