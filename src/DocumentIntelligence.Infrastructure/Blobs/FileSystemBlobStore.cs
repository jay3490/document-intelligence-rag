using DocumentIntelligence.Core.Storage;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Infrastructure.Blobs;

/// <summary>
/// Stores blobs as files below a root directory. Writes go to a temporary file that is then
/// moved into place, so readers never see a half-written blob.
/// </summary>
public sealed class FileSystemBlobStore : IBlobStore
{
    private readonly string _root;

    public FileSystemBlobStore(IOptions<BlobStorageOptions> options)
    {
        _root = Path.GetFullPath(options.Value.FileSystem.RootPath);
    }

    public async Task PutAsync(string name, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = PathFor(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await content.CopyToAsync(file, cancellationToken);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public Task<Stream?> OpenReadAsync(string name, CancellationToken cancellationToken)
    {
        var path = PathFor(name);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        var path = PathFor(name);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string PathFor(string name)
    {
        BlobNames.EnsureValid(name);
        var path = Path.GetFullPath(Path.Combine(_root, name.Replace('/', Path.DirectorySeparatorChar)));

        // Defence in depth: validation already rejects "..", but never leave the root.
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{name}' resolves outside the blob root.", nameof(name));
        }

        return path;
    }
}
