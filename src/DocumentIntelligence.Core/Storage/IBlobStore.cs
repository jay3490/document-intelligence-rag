using System.Text.RegularExpressions;

namespace DocumentIntelligence.Core.Storage;

/// <summary>
/// Holds the raw content of uploaded documents until (and after) they are ingested, so the
/// ingestion event only has to carry a reference rather than the document itself.
/// </summary>
public interface IBlobStore
{
    /// <summary>Writes a blob, replacing any blob with the same name.</summary>
    Task PutAsync(string name, Stream content, CancellationToken cancellationToken);

    /// <returns>The blob's content, or null when no blob has that name.</returns>
    Task<Stream?> OpenReadAsync(string name, CancellationToken cancellationToken);

    /// <summary>Removes a blob; removing a missing blob is not an error.</summary>
    Task DeleteAsync(string name, CancellationToken cancellationToken);
}

/// <summary>Builds and validates blob names, which are relative, '/'-separated paths.</summary>
public static partial class BlobNames
{
    /// <summary>The name an uploaded document is stored under: <c>{tenant}/{documentId:N}/{fileName}</c>.</summary>
    public static string ForDocument(string tenantId, Guid documentId, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var name = $"{tenantId}/{documentId:N}/{Path.GetFileName(fileName)}";
        EnsureValid(name);
        return name;
    }

    /// <summary>
    /// Rejects names that could escape a container or a root directory: empty segments, "." or
    /// "..", backslashes, control characters and leading slashes.
    /// </summary>
    public static void EnsureValid(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 1024 ||
            name.StartsWith('/') ||
            name.Contains('\\') ||
            name.Any(char.IsControl) ||
            name.Split('/').Any(segment => segment.Length == 0 || segment is "." or ".." || !SegmentPattern().IsMatch(segment)))
        {
            throw new ArgumentException($"'{name}' is not a valid blob name.", nameof(name));
        }
    }

    [GeneratedRegex(@"^[^:*?""<>|]+$")]
    private static partial Regex SegmentPattern();
}
