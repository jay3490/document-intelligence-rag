using System.Text;
using DocumentIntelligence.Api.Ingestion;
using DocumentIntelligence.Core.Documents;
using DocumentIntelligence.Core.Ingestion;
using DocumentIntelligence.Core.Search;
using DocumentIntelligence.Core.Tenancy;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Api.Endpoints;

public static class DocumentEndpoints
{
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

        ingestions.MapGet("/", (IngestionJobStore jobs, ITenantContext tenant) => Results.Ok(jobs.List(tenant.TenantId)))
            .WithSummary("List the current tenant's recent ingestion jobs, newest first.");

        ingestions.MapGet("/{jobId:guid}", (Guid jobId, IngestionJobStore jobs, ITenantContext tenant) =>
                jobs.Get(jobId) is { } job && job.TenantId == tenant.TenantId
                    ? Results.Ok(job)
                    : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Ingestion job not found"))
            .WithName("GetIngestionJob")
            .WithSummary("Get the status of one ingestion job.");

        return app;
    }

    private static async Task<IResult> UploadAsync(
        HttpRequest request,
        IngestionQueue queue,
        IngestionJobStore jobs,
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
        if (!DocumentFormats.TryFromFileName(fileName, out var format))
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

        var document = new SourceDocument(Guid.NewGuid(), fileName, format, content)
        {
            TenantId = tenant.TenantId,
            Metadata = metadata,
        };
        var job = jobs.Create(tenant.TenantId, document.Id, fileName, file.Length);

        if (!queue.TryEnqueue(new IngestionWorkItem(job.Id, document)))
        {
            jobs.Remove(job.Id);
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Ingestion queue is full",
                detail: "Too many documents are waiting to be ingested. Try again shortly.");
        }

        return Results.AcceptedAtRoute("GetIngestionJob", new { jobId = job.Id }, job);
    }

    private static async Task<IResult> DeleteAsync(
        Guid documentId,
        IngestionPipeline pipeline,
        Bm25Index index,
        ITenantContext tenant,
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
