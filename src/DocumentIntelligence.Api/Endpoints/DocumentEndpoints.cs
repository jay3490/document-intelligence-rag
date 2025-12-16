using System.Security.Cryptography;
using System.Text;
using DocumentIntelligence.Api.Ingestion;
using DocumentIntelligence.Core.Answering;
using DocumentIntelligence.Core.Documents;
using DocumentIntelligence.Core.Ingestion;
using DocumentIntelligence.Core.Messaging;
using DocumentIntelligence.Core.Search;
using DocumentIntelligence.Core.Storage;
using DocumentIntelligence.Core.Tenancy;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Api.Endpoints;

public static class DocumentEndpoints
{
    public const int MaxListedJobs = 100;

    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var documents = app.MapGroup("/api/documents").WithTags("Documents");

        documents.MapPost("/", UploadAsync)
            .DisableAntiforgery()
            .WithSummary("Upload a .txt or .md document, with optional tags, source and date, for background ingestion.");

        documents.MapGet("/", (Bm25Index index, ITenantContext tenant) => Results.Ok(index.ListDocuments(tenant.TenantId)))
            .WithSummary("List the current tenant's documents in the search index.");

        documents.MapDelete("/{documentId:guid}", DeleteAsync)
            .WithSummary("Remove a document and all of its chunks.");

        var ingestions = app.MapGroup("/api/ingestions").WithTags("Ingestion");

        ingestions.MapGet("/", async (IIngestionJobStore jobs, ITenantContext tenant, CancellationToken cancellationToken) =>
                Results.Ok(await jobs.ListAsync(tenant.TenantId, MaxListedJobs, cancellationToken)))
            .WithSummary("List the current tenant's recent ingestion jobs, newest first.");

        ingestions.MapGet("/{jobId:guid}", async (Guid jobId, IIngestionJobStore jobs, ITenantContext tenant, CancellationToken cancellationToken) =>
                await jobs.GetAsync(jobId, cancellationToken) is { } job && job.TenantId == tenant.TenantId
                    ? Results.Ok(job)
                    : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Ingestion job not found"))
            .WithName("GetIngestionJob")
            .WithSummary("Get the status of one ingestion job.");

        return app;
    }

    private static async Task<IResult> UploadAsync(
        HttpRequest request,
        IIngestionEventPublisher publisher,
        IIngestionJobStore jobs,
        IBlobStore blobs,
        ITenantContext tenant,
        TimeProvider timeProvider,
        IOptions<IngestionOptions> options,
        CancellationToken cancellationToken)
    {
        const string MissingFile = "Attach the document as multipart form data in a field named 'file'.";
        if (!request.HasFormContentType)
        {
            return Invalid("file", MissingFile);
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null)
        {
            return Invalid("file", MissingFile);
        }

        var fileName = Path.GetFileName(file.FileName);
        if (!DocumentFormats.TryFromFileName(fileName, out _))
        {
            return Invalid("file", $"Unsupported file type. Supported extensions: {string.Join(", ", DocumentFormats.SupportedExtensions)}.");
        }

        var maxBytes = options.Value.MaxDocumentBytes;
        if (file.Length > maxBytes)
        {
            return Invalid("file", $"The file is {file.Length} bytes; the limit is {maxBytes} bytes.");
        }

        var errors = new Dictionary<string, string[]>();
        var metadata = MetadataInput.ParseUploadFields(form, timeProvider.GetUtcNow(), errors);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var content = await ReadUtf8Async(file, cancellationToken);
        if (content is null || content.Contains('\0'))
        {
            return Invalid("file", "The file is not UTF-8 text.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return Invalid("file", "The file contains no text.");
        }

        // The raw content goes to the blob store; the message only carries a reference to it.
        var bytes = Encoding.UTF8.GetBytes(content);
        var documentId = Guid.NewGuid();
        var message = new DocumentUploaded
        {
            JobId = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            DocumentId = documentId,
            FileName = fileName,
            BlobName = BlobNames.ForDocument(tenant.TenantId, documentId, fileName),
            ContentHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            SizeBytes = file.Length,
            Source = metadata.Source,
            Tags = metadata.Tags,
            Date = metadata.Date,
            UploadedAt = timeProvider.GetUtcNow(),
            CorrelationId = request.HttpContext.TraceIdentifier,
        };

        using (var stream = new MemoryStream(bytes, writable: false))
        {
            await blobs.PutAsync(message.BlobName, stream, cancellationToken);
        }

        // The job exists before the message is sent, so its status can be read right away.
        var job = await jobs.AddAsync(message.ToNewJob(), cancellationToken);
        try
        {
            await publisher.PublishUploadedAsync(message, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await jobs.DeleteAsync(job.Id, CancellationToken.None);
            await blobs.DeleteAsync(message.BlobName, CancellationToken.None);
            if (exception is IngestionBackpressureException)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Ingestion queue is full",
                    detail: exception.Message);
            }

            throw;
        }

        return Results.AcceptedAtRoute("GetIngestionJob", new { jobId = job.Id }, job);
    }

    private static async Task<IResult> DeleteAsync(
        Guid documentId,
        IngestionPipeline pipeline,
        Bm25Index index,
        ITenantContext tenant,
        IQueryResultCache queryCache,
        IIngestionJobStore jobs,
        IIngestionEventPublisher publisher,
        TimeProvider timeProvider,
        HttpContext httpContext,
        KeywordIndexInitializer keywordIndexInitializer,
        CancellationToken cancellationToken)
    {
        // Until the start-up rebuild finishes it could re-add chunks of a document deleted now.
        if (!keywordIndexInitializer.IsReady)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Search index is still loading",
                detail: "Documents can be removed once the index has been rebuilt from the vector store.");
        }

        // Another tenant's document is reported exactly like a missing one.
        if (index.FindDocumentTenant(documentId) != tenant.TenantId)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Document not found");
        }

        await pipeline.RemoveAsync(documentId, cancellationToken);
        await jobs.MarkDocumentRemovedAsync(documentId, cancellationToken);
        await queryCache.InvalidateTenantAsync(tenant.TenantId, cancellationToken);
        await publisher.PublishIndexChangedAsync(
            new DocumentIndexChanged(tenant.TenantId, documentId, IndexChange.Removed, timeProvider.GetUtcNow(), httpContext.TraceIdentifier),
            cancellationToken);
        return Results.NoContent();
    }

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static async Task<string?> ReadUtf8Async(IFormFile file, CancellationToken cancellationToken)
    {
        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        using var reader = new StreamReader(file.OpenReadStream(), strictUtf8, detectEncodingFromByteOrderMarks: true);
        try
        {
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
