using DocumentIntelligence.Core.Documents;

namespace DocumentIntelligence.Core.Search;

/// <summary>
/// An in-memory inverted index that ranks chunks with Okapi BM25.
/// </summary>
/// <remarks>
/// The index is thread-safe. Adding a chunk whose id is already present replaces it, which
/// keeps re-ingestion and start-up rebuilds idempotent. IDF uses the non-negative variant
/// <c>ln(1 + (N - df + 0.5) / (df + 0.5))</c> so very common terms never subtract score.
/// </remarks>
public sealed class Bm25Index
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, IndexedChunk> _chunks = [];
    private readonly Dictionary<string, HashSet<Guid>> _postings = new(StringComparer.Ordinal);
    private readonly double _k1;
    private readonly double _b;
    private long _totalLength;

    /// <param name="k1">Term-frequency saturation. Higher values let repeated terms count for more.</param>
    /// <param name="b">Length normalisation, from 0 (none) to 1 (full).</param>
    public Bm25Index(double k1 = 1.2, double b = 0.75)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(k1);
        ArgumentOutOfRangeException.ThrowIfNegative(b);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(b, 1);
        _k1 = k1;
        _b = b;
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _chunks.Count;
            }
        }
    }

    public void Add(IEnumerable<DocumentChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        lock (_gate)
        {
            foreach (var chunk in chunks)
            {
                RemoveChunk(chunk.Id);

                var frequencies = new Dictionary<string, int>(StringComparer.Ordinal);
                var length = 0;
                foreach (var term in TextAnalyzer.Terms(chunk.SearchText))
                {
                    frequencies[term] = frequencies.GetValueOrDefault(term) + 1;
                    length++;
                }

                _chunks[chunk.Id] = new IndexedChunk(chunk, frequencies, length);
                _totalLength += length;
                foreach (var term in frequencies.Keys)
                {
                    if (!_postings.TryGetValue(term, out var ids))
                    {
                        _postings[term] = ids = [];
                    }

                    ids.Add(chunk.Id);
                }
            }
        }
    }

    /// <summary>Summarises the indexed documents, ordered by name.</summary>
    /// <param name="tenantId">Only documents of this tenant; null lists every tenant's documents.</param>
    public IReadOnlyList<IndexedDocument> ListDocuments(string? tenantId = null)
    {
        lock (_gate)
        {
            return _chunks.Values
                .Where(c => tenantId is null || c.Chunk.TenantId == tenantId)
                .GroupBy(c => c.Chunk.DocumentId)
                .Select(g => Summarise(g.Key, g.Select(c => c.Chunk).ToList()))
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.Id)
                .ToList();
        }
    }

    /// <summary>The owning tenant of an indexed document, or null when the document is not indexed.</summary>
    public string? FindDocumentTenant(Guid documentId)
    {
        lock (_gate)
        {
            return _chunks.Values.FirstOrDefault(c => c.Chunk.DocumentId == documentId)?.Chunk.TenantId;
        }
    }

    /// <returns>The number of chunks removed.</returns>
    public int RemoveDocument(Guid documentId)
    {
        lock (_gate)
        {
            var ids = _chunks.Values
                .Where(c => c.Chunk.DocumentId == documentId)
                .Select(c => c.Chunk.Id)
                .ToList();

            foreach (var id in ids)
            {
                RemoveChunk(id);
            }

            return ids.Count;
        }
    }

    /// <param name="filter">When given, only chunks it matches are scored.</param>
    public IReadOnlyList<ScoredChunk> Search(string query, int limit, SearchFilter? filter = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var queryTerms = TextAnalyzer.Terms(query).Distinct(StringComparer.Ordinal).ToList();

        lock (_gate)
        {
            if (queryTerms.Count == 0 || _chunks.Count == 0)
            {
                return [];
            }

            var documentCount = _chunks.Count;
            var averageLength = Math.Max(1.0, (double)_totalLength / documentCount);
            var scores = new Dictionary<Guid, double>();

            foreach (var term in queryTerms)
            {
                if (!_postings.TryGetValue(term, out var ids))
                {
                    continue;
                }

                var idf = Math.Log(1 + ((documentCount - ids.Count + 0.5) / (ids.Count + 0.5)));
                foreach (var id in ids)
                {
                    var indexed = _chunks[id];
                    if (filter is not null && !filter.Matches(indexed.Chunk))
                    {
                        continue;
                    }

                    double frequency = indexed.TermFrequencies[term];
                    var normaliser = _k1 * (1 - _b + (_b * indexed.Length / averageLength));
                    scores[id] = scores.GetValueOrDefault(id) + (idf * frequency * (_k1 + 1) / (frequency + normaliser));
                }
            }

            return scores
                .Select(pair => new ScoredChunk(_chunks[pair.Key].Chunk, pair.Value))
                .OrderByDescending(hit => hit.Score)
                .ThenBy(hit => hit.Chunk.DocumentName, StringComparer.Ordinal)
                .ThenBy(hit => hit.Chunk.ChunkIndex)
                .Take(limit)
                .ToList();
        }
    }

    private static IndexedDocument Summarise(Guid documentId, IReadOnlyList<DocumentChunk> chunks)
    {
        var first = chunks[0];
        return new IndexedDocument(documentId, first.DocumentName, chunks.Count, chunks.Sum(c => c.TokenCount))
        {
            Source = first.Metadata.Source,
            Tags = first.Metadata.Tags,
            Date = first.Metadata.Date,
        };
    }

    private void RemoveChunk(Guid chunkId)
    {
        if (!_chunks.Remove(chunkId, out var existing))
        {
            return;
        }

        _totalLength -= existing.Length;
        foreach (var term in existing.TermFrequencies.Keys)
        {
            var ids = _postings[term];
            ids.Remove(chunkId);
            if (ids.Count == 0)
            {
                _postings.Remove(term);
            }
        }
    }

    private sealed record IndexedChunk(DocumentChunk Chunk, Dictionary<string, int> TermFrequencies, int Length);
}
