using System.Text;
using DocumentIntelligence.Core.Storage;
using DocumentIntelligence.Infrastructure.Blobs;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.UnitTests.Infrastructure;

public sealed class BlobStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "di-blob-tests", Guid.NewGuid().ToString("N"));
    private readonly FileSystemBlobStore _store;

    public BlobStoreTests() =>
        _store = new FileSystemBlobStore(Options.Create(new BlobStorageOptions { FileSystem = { RootPath = _root } }));

    [Fact]
    public async Task Round_trips_content_and_overwrites_existing_blobs()
    {
        await PutAsync("acme/doc/notes.md", "first");
        await PutAsync("acme/doc/notes.md", "second");

        Assert.Equal("second", await ReadAsync("acme/doc/notes.md"));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "acme", "doc")));
    }

    [Fact]
    public async Task Missing_blobs_read_as_null_and_delete_quietly()
    {
        Assert.Null(await _store.OpenReadAsync("acme/missing.txt", CancellationToken.None));

        await _store.DeleteAsync("acme/missing.txt", CancellationToken.None);
    }

    [Fact]
    public async Task Deleting_removes_the_blob()
    {
        await PutAsync("acme/doc/a.txt", "text");

        await _store.DeleteAsync("acme/doc/a.txt", CancellationToken.None);

        Assert.Null(await _store.OpenReadAsync("acme/doc/a.txt", CancellationToken.None));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("acme/../../escape.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("acme\\windows.txt")]
    [InlineData("acme//double.txt")]
    [InlineData("acme/c:drive.txt")]
    public async Task Names_that_could_escape_the_root_are_rejected(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _store.OpenReadAsync(name, CancellationToken.None));
    }

    [Fact]
    public void Document_blob_names_are_scoped_by_tenant_and_document()
    {
        var id = Guid.Parse("8f1d1c6e-4c5b-4f44-9f0b-2a6c1b1d0e11");

        Assert.Equal("acme/8f1d1c6e4c5b4f449f0b2a6c1b1d0e11/notes.md", BlobNames.ForDocument("acme", id, "../../notes.md"));
    }

    [Fact]
    public void Azure_container_clients_are_built_from_a_connection_string()
    {
        var client = AzureBlobStore.CreateContainerClient(new AzureBlobOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            ContainerName = "uploads",
        });

        Assert.Equal("uploads", client.Name);
    }

    [Fact]
    public void Azure_storage_requires_a_connection_string_or_service_uri()
    {
        var options = new BlobStorageOptions { Provider = BlobStorageProvider.AzureBlob };
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        Assert.False(System.ComponentModel.DataAnnotations.Validator.TryValidateObject(options, new(options), results, validateAllProperties: true));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private async Task PutAsync(string name, string text)
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes(text));
        await _store.PutAsync(name, content, CancellationToken.None);
    }

    private async Task<string> ReadAsync(string name)
    {
        await using var stream = await _store.OpenReadAsync(name, CancellationToken.None);
        using var reader = new StreamReader(stream!);
        return await reader.ReadToEndAsync();
    }
}
