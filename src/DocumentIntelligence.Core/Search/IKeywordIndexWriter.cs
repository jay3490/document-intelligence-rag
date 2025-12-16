using DocumentIntelligence.Core.Documents;

namespace DocumentIntelligence.Core.Search;

/// <summary>The write side of the keyword index, as used by the ingestion pipeline.</summary>
public interface IKeywordIndexWriter
{
    void Add(IEnumerable<DocumentChunk> chunks);

    /// <returns>The number of chunks removed.</returns>
    int RemoveDocument(Guid documentId);
}

/// <summary>
/// For processes that ingest but never answer queries, such as the ingestion worker. Query
/// services keep their own keyword index current from the vector store instead.
/// </summary>
public sealed class NoKeywordIndexWriter : IKeywordIndexWriter
{
    public void Add(IEnumerable<DocumentChunk> chunks)
    {
    }

    public int RemoveDocument(Guid documentId) => 0;
}
