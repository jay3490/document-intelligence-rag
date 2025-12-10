using Azure;
using Azure.Storage.Blobs;
using DocumentIntelligence.Core;
using DocumentIntelligence.Core.Storage;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Infrastructure.Blobs;

/// <summary>Stores blobs in one Azure Blob Storage container, created on first use.</summary>
public sealed class AzureBlobStore : IBlobStore
{
    private const string Service = "blob-storage";

    private readonly BlobContainerClient _container;
    private readonly SemaphoreSlim _createGate = new(1, 1);
    private volatile bool _containerReady;

    public AzureBlobStore(IOptions<BlobStorageOptions> options)
        : this(CreateContainerClient(options.Value.AzureBlob))
    {
    }

    public AzureBlobStore(BlobContainerClient container)
    {
        _container = container;
    }

    public async Task PutAsync(string name, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        BlobNames.EnsureValid(name);
        await EnsureContainerAsync(cancellationToken);

        await CallAsync(() => _container.GetBlobClient(name).UploadAsync(content, overwrite: true, cancellationToken));
    }

    public async Task<Stream?> OpenReadAsync(string name, CancellationToken cancellationToken)
    {
        BlobNames.EnsureValid(name);
        try
        {
            return await _container.GetBlobClient(name).OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
        catch (RequestFailedException exception)
        {
            throw new UpstreamServiceException(Service, $"Reading blob '{name}' failed: {exception.Message}", exception);
        }
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        BlobNames.EnsureValid(name);
        await CallAsync(() => _container.GetBlobClient(name).DeleteIfExistsAsync(cancellationToken: cancellationToken));
    }

    internal static BlobContainerClient CreateContainerClient(AzureBlobOptions options) =>
        !string.IsNullOrWhiteSpace(options.ConnectionString)
            ? new BlobContainerClient(options.ConnectionString, options.ContainerName)
            : new BlobServiceClient(options.ServiceUri ?? throw new InvalidOperationException("AzureBlob:ServiceUri is not set."))
                .GetBlobContainerClient(options.ContainerName);

    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (_containerReady)
        {
            return;
        }

        await _createGate.WaitAsync(cancellationToken);
        try
        {
            if (!_containerReady)
            {
                await CallAsync(() => _container.CreateIfNotExistsAsync(cancellationToken: cancellationToken));
                _containerReady = true;
            }
        }
        finally
        {
            _createGate.Release();
        }
    }

    private static async Task CallAsync<T>(Func<Task<T>> call)
    {
        try
        {
            await call();
        }
        catch (RequestFailedException exception)
        {
            throw new UpstreamServiceException(Service, $"Azure Blob Storage returned {exception.Status}: {exception.Message}", exception);
        }
    }
}
