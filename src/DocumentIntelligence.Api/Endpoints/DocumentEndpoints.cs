using System.Text;
using DocumentIntelligence.Api.Ingestion;
using DocumentIntelligence.Core.Documents;
using DocumentIntelligence.Core.Ingestion;
using DocumentIntelligence.Core.Search;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Api.Endpoints;

public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var documents = app.MapGroup("/api/documents").WithTags("Documents");

        documents.MapPost("/", UploadAsync)
            .DisableAntiforgery()
            .WithSummary("Upload a .txt or .md document for background ingestion.");

        documents.MapGet("/", (Bm25Index index) => Results.Ok(index.ListDocuments()))
            .WithSummary("List the documents currently in the search index.");

        documents.MapDelete("/{documentId:guid}", DeleteAsync)
            .WithSummary("Remove a document and all of its chunks.");

        var ingestions = app.MapGroup("/api/ingestions").WithTags("Ingestion");

        ingestions.MapGet("/", (IngestionJobStore jobs) => Results.Ok(jobs.List()))
            .WithSummary("List recent ingestion jobs, newest first.");

        ingestions.MapGet("/{jobId:guid}", (Guid jobId, IngestionJobStore jobs) =>
                jobs.Get(jobId) is { } job
                    ? Results.Ok(job)
                    : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Ingestion job not found"))
            .WithName("GetIngestionJob")
            .WithSummary("Get the status of one ingestion job.");

        return app;
    }

    private static async Task<IResult> UploadAsync(
        IFormFile? file,
        IngestionQueue queue,
        IngestionJobStore jobs,
        IOptions<IngestionOptions> options,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return Invalid("Attach the document as multipart form data in a field named 'file'.");
        }

        var fileName = Path.GetFileName(file.FileName);
        if (!DocumentFormats.TryFromFileName(fileName, out var format))
        {
            return Invalid($"Unsupported file type. Supported extensions: {string.Join(", ", DocumentFormats.SupportedExtensions)}.");
        }

        var maxBytes = options.Value.MaxDocumentBytes;
        if (file.Length > maxBytes)
        {
            return Invalid($"The file is {file.Length} bytes; the limit is {maxBytes} bytes.");
        }

        var content = await ReadUtf8Async(file, cancellationToken);
        if (content is null || content.Contains('\0'))
        {
            return Invalid("The file is not UTF-8 text.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return Invalid("The file contains no text.");
        }

        var document = new SourceDocument(Guid.NewGuid(), fileName, format, content);
        var job = jobs.Create(document.Id, fileName, file.Length);

        if (!queue.TryEnqueue(new IngestionWorkItem(job.Id, document)))
        {
            jobs.Remove(job.Id);
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Ingestion queue is full",
                detail: "Too many documents are waiting to be ingested. Try again shortly.");
        }

        return Results.AcceptedAtRoute("GetIngestionJob", new { jobId = job.Id }, job);

        static IResult Invalid(string message) =>
            Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [message] });
    }

    private static async Task<IResult> DeleteAsync(
        Guid documentId,
        IngestionPipeline pipeline,
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

        await pipeline.RemoveAsync(documentId, cancellationToken);
        return Results.NoContent();
    }

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
