using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DocumentIntelligence.Core.Documents;
using DocumentIntelligence.Core.Ingestion;
using DocumentIntelligence.Core.Messaging;
using DocumentIntelligence.Core.Storage;
using DocumentIntelligence.Core.Tenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Functions.Drops;

/// <summary>A file that appeared in the drop container, at <c>{tenant}/{file name}</c>.</summary>
public sealed record BlobDrop(string Path, byte[] Content, IReadOnlyDictionary<string, string> Metadata);

public enum DropOutcome
{
    Published,
    Rejected,
}

public sealed record DropResult(DropOutcome Outcome, Guid? JobId, string? Reason)
{
    public static DropResult Rejected(string reason) => new(DropOutcome.Rejected, null, reason);
}

/// <summary>
/// Turns a dropped file into a <see cref="DocumentUploaded"/> message: validates it, copies it
/// into the document blob store under the usual name and publishes the message for the worker.
/// </summary>
/// <remarks>
/// Blob triggers can fire more than once for the same blob. Job and document ids are derived
/// from the blob path and a hash of its content, so a repeated trigger republishes the same job,
/// which the worker recognises and skips; a changed file gets a new job. Optional blob metadata
/// <c>tags</c> (comma-separated), <c>source</c> and <c>date</c> become document metadata.
/// </remarks>
public sealed partial class BlobDropHandler(
    IBlobStore blobs,
    IIngestionEventPublisher publisher,
    IOptions<IngestionOptions> options,
    TimeProvider timeProvider,
    ILogger<BlobDropHandler> logger)
{
    public const string DefaultSource = "blob-drop";

    public async Task<DropResult> HandleAsync(BlobDrop drop, string correlationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(drop);

        var separator = drop.Path.IndexOf('/', StringComparison.Ordinal);
        var tenant = separator > 0 ? drop.Path[..separator] : string.Empty;
        var fileName = System.IO.Path.GetFileName(drop.Path);

        var problem = Validate(tenant, fileName, drop.Content);
        if (problem is not null)
        {
            LogRejected(logger, drop.Path, problem);
            return DropResult.Rejected(problem);
        }

        var contentHash = Convert.ToHexString(SHA256.HashData(drop.Content)).ToLowerInvariant();
        var jobId = DeterministicId($"job\n{drop.Path}\n{contentHash}");
        var documentId = DeterministicId($"document\n{drop.Path}\n{contentHash}");
        var uploadedAt = timeProvider.GetUtcNow();
        var metadata = ReadMetadata(drop.Metadata, uploadedAt);

        var message = new DocumentUploaded
        {
            JobId = jobId,
            TenantId = tenant,
            DocumentId = documentId,
            FileName = fileName,
            BlobName = BlobNames.ForDocument(tenant, documentId, fileName),
            ContentHash = contentHash,
            SizeBytes = drop.Content.Length,
            Source = metadata.Source,
            Tags = metadata.Tags,
            Date = metadata.Date,
            UploadedAt = uploadedAt,
            CorrelationId = correlationId,
        };

        using (var stream = new MemoryStream(drop.Content, writable: false))
        {
            await blobs.PutAsync(message.BlobName, stream, cancellationToken);
        }

        await publisher.PublishUploadedAsync(message, cancellationToken);
        LogPublished(logger, drop.Path, jobId);
        return new DropResult(DropOutcome.Published, jobId, null);
    }

    private string? Validate(string tenant, string fileName, byte[] content)
    {
        if (!TenantId.IsValid(tenant))
        {
            return "Files must be dropped under a valid tenant folder, for example 'acme/handbook.md'.";
        }

        if (!DocumentFormats.TryFromFileName(fileName, out _))
        {
            return $"Unsupported file type. Supported extensions: {string.Join(", ", DocumentFormats.SupportedExtensions)}.";
        }

        if (content.Length > options.Value.MaxDocumentBytes)
        {
            return $"The file is {content.Length} bytes; the limit is {options.Value.MaxDocumentBytes} bytes.";
        }

        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return "The file is not UTF-8 text.";
        }

        return string.IsNullOrWhiteSpace(text.TrimStart('﻿')) || text.Contains('\0') ? "The file contains no text." : null;
    }

    private static DocumentMetadata ReadMetadata(IReadOnlyDictionary<string, string> metadata, DateTimeOffset uploadedAt)
    {
        var lookup = metadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var tags = lookup.TryGetValue("tags", out var rawTags) ? rawTags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [];
        var source = lookup.TryGetValue("source", out var rawSource) && !string.IsNullOrWhiteSpace(rawSource)
            ? rawSource.Trim()[..Math.Min(rawSource.Trim().Length, DocumentMetadata.MaxSourceLength)]
            : DefaultSource;
        var date = lookup.TryGetValue("date", out var rawDate) &&
                   DateTimeOffset.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : uploadedAt;

        return new DocumentMetadata { Tags = tags.Where(tag => tag.Length <= DocumentMetadata.MaxTagLength).Take(DocumentMetadata.MaxTags).ToList(), Source = source, Date = date };
    }

    private static Guid DeterministicId(string material)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(material), hash);
        return new Guid(hash[..16]);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected dropped file {Path}: {Reason}")]
    private static partial void LogRejected(ILogger logger, string path, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dropped file {Path} published as ingestion job {JobId}")]
    private static partial void LogPublished(ILogger logger, string path, Guid jobId);
}
