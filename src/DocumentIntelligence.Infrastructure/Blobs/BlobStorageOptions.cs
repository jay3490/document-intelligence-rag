using System.ComponentModel.DataAnnotations;

namespace DocumentIntelligence.Infrastructure.Blobs;

public enum BlobStorageProvider
{
    /// <summary>A directory on local disk (or a mounted volume shared by API and worker).</summary>
    FileSystem,

    /// <summary>An Azure Blob Storage container.</summary>
    AzureBlob,
}

public sealed class BlobStorageOptions : IValidatableObject
{
    public const string SectionName = "BlobStorage";

    public BlobStorageProvider Provider { get; set; } = BlobStorageProvider.FileSystem;

    public FileSystemBlobOptions FileSystem { get; set; } = new();

    public AzureBlobOptions AzureBlob { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Provider == BlobStorageProvider.FileSystem && string.IsNullOrWhiteSpace(FileSystem.RootPath))
        {
            yield return new ValidationResult("FileSystem:RootPath is required.", ["FileSystem.RootPath"]);
        }

        if (Provider == BlobStorageProvider.AzureBlob)
        {
            if (string.IsNullOrWhiteSpace(AzureBlob.ConnectionString) && AzureBlob.ServiceUri is null)
            {
                yield return new ValidationResult(
                    "AzureBlob needs either ConnectionString or ServiceUri.", ["AzureBlob.ConnectionString", "AzureBlob.ServiceUri"]);
            }

            if (string.IsNullOrWhiteSpace(AzureBlob.ContainerName))
            {
                yield return new ValidationResult("AzureBlob:ContainerName is required.", ["AzureBlob.ContainerName"]);
            }
        }
    }
}

public sealed class FileSystemBlobOptions
{
    /// <summary>Blobs are stored as files below this directory. Relative paths resolve against the working directory.</summary>
    public string RootPath { get; set; } = "data/blobs";
}

public sealed class AzureBlobOptions
{
    /// <summary>A storage account connection string; use this or <see cref="ServiceUri"/>.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// A container-level or account-level blob endpoint carrying a SAS token, for example
    /// https://account.blob.core.windows.net/?sv=...; use this or <see cref="ConnectionString"/>.
    /// </summary>
    public Uri? ServiceUri { get; set; }

    public string ContainerName { get; set; } = "documents";
}
