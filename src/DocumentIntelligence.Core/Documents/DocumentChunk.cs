using System.Security.Cryptography;
using System.Text;

namespace DocumentIntelligence.Core.Documents;

/// <summary>
/// A retrievable slice of a document. <see cref="Section"/> holds the Markdown heading path
/// the chunk was found under, when there is one.
/// </summary>
public sealed record DocumentChunk(
    Guid Id,
    Guid DocumentId,
    string DocumentName,
    int ChunkIndex,
    string? Section,
    string Text,
    int TokenCount)
{
    /// <summary>The tenant that owns the chunk; searches only ever see their own tenant's chunks.</summary>
    public string TenantId { get; init; } = Tenancy.TenantId.Default;

    /// <summary>The metadata of the document the chunk was cut from.</summary>
    public DocumentMetadata Metadata { get; init; } = DocumentMetadata.None;

    /// <summary>
    /// The text used for embedding and keyword indexing. The section heading is prepended so
    /// that a chunk deep inside a section still matches queries that name the section.
    /// </summary>
    public string SearchText => Section is null ? Text : $"{Section}\n\n{Text}";

    /// <summary>
    /// Chunk ids are derived from the document id and the chunk position, so re-ingesting a
    /// document overwrites its previous chunks instead of duplicating them.
    /// </summary>
    public static Guid CreateId(Guid documentId, int chunkIndex)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes($"{documentId:N}:{chunkIndex}"), hash);
        return new Guid(hash[..16]);
    }
}
