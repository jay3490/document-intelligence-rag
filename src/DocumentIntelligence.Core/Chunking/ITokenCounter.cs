namespace DocumentIntelligence.Core.Chunking;

/// <summary>Counts model tokens so chunks can be sized against an embedding model's budget.</summary>
public interface ITokenCounter
{
    int CountTokens(string text);
}
