using DocumentIntelligence.Core.Chunking;
using Microsoft.ML.Tokenizers;

namespace DocumentIntelligence.Infrastructure.Tokenization;

/// <summary>
/// Counts tokens with the o200k_base byte-pair encoding. The encoding ships inside a NuGet
/// package, so counting works offline. For models with a different tokenizer the counts are
/// an approximation, which is good enough for sizing chunks.
/// </summary>
public sealed class TiktokenTokenCounter : ITokenCounter
{
    private readonly Tokenizer _tokenizer = TiktokenTokenizer.CreateForEncoding("o200k_base");

    public int CountTokens(string text) => _tokenizer.CountTokens(text);
}
