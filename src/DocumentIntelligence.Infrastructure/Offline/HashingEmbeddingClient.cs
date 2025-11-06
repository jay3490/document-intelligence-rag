using DocumentIntelligence.Core.Embeddings;
using DocumentIntelligence.Core.Search;

namespace DocumentIntelligence.Infrastructure.Offline;

/// <summary>
/// Deterministic embeddings built with signed feature hashing, so the system runs end to end
/// without a model or credentials.
/// </summary>
/// <remarks>
/// Every term and every character trigram of a term is hashed into one of the vector's
/// dimensions with a hash-derived sign, and the vector is L2-normalised. Texts that share
/// words or word fragments end up close together. This captures lexical overlap only; it has
/// no notion of meaning, which is what a real embedding model adds.
/// </remarks>
public sealed class HashingEmbeddingClient : IEmbeddingClient
{
    private const float TrigramWeight = 0.35f;
    private readonly int _dimensions;

    public HashingEmbeddingClient(int dimensions)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dimensions, 8);
        _dimensions = dimensions;
    }

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        IReadOnlyList<float[]> vectors = texts.Select(Embed).ToList();
        return Task.FromResult(vectors);
    }

    private float[] Embed(string text)
    {
        var vector = new float[_dimensions];

        foreach (var term in TextAnalyzer.Terms(text))
        {
            Accumulate(vector, term, 1f);

            var padded = $"^{term}$";
            for (var i = 0; i + 3 <= padded.Length; i++)
            {
                Accumulate(vector, padded.Substring(i, 3), TrigramWeight);
            }
        }

        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm == 0)
        {
            // Text without any terms still needs a valid direction for cosine similarity.
            vector[0] = 1f;
            return vector;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= norm;
        }

        return vector;
    }

    private void Accumulate(float[] vector, string feature, float weight)
    {
        var hash = Fnv1a(feature);
        var index = (int)(hash % (uint)_dimensions);
        var sign = (hash & 0x8000_0000) == 0 ? 1f : -1f;
        vector[index] += sign * weight;
    }

    /// <summary>FNV-1a over UTF-16 code units; stable across processes, unlike string.GetHashCode.</summary>
    private static uint Fnv1a(string value)
    {
        var hash = 2166136261u;
        foreach (var c in value)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return hash;
    }
}
