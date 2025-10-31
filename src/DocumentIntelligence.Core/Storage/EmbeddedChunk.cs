using DocumentIntelligence.Core.Documents;

namespace DocumentIntelligence.Core.Storage;

public sealed record EmbeddedChunk(DocumentChunk Chunk, float[] Vector);
