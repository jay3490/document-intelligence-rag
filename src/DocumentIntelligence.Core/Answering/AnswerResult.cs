namespace DocumentIntelligence.Core.Answering;

/// <summary>
/// A generated answer. <see cref="Citation.Number"/> matches the bracketed markers, such as
/// [2], that appear in <see cref="Answer"/>.
/// </summary>
public sealed record AnswerResult(string Answer, IReadOnlyList<Citation> Citations);

public sealed record Citation(
    int Number,
    Guid ChunkId,
    Guid DocumentId,
    string DocumentName,
    string? Section,
    int ChunkIndex,
    string Text,
    double Score);
