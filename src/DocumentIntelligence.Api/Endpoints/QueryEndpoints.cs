using DocumentIntelligence.Core.Answering;
using DocumentIntelligence.Core.Tenancy;

namespace DocumentIntelligence.Api.Endpoints;

public sealed record QueryRequest(string? Question, int? TopK, QueryFilters? Filters = null);

public static class QueryEndpoints
{
    public const int MaxQuestionLength = 2000;
    public const int MaxTopK = 20;

    /// <summary>Response header that says whether the answer came from the query cache.</summary>
    public const string CacheHeader = "X-Cache";

    public static IEndpointRouteBuilder MapQueryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/query", QueryAsync)
            .WithTags("Query")
            .WithSummary("Answer a question from the current tenant's documents, optionally filtered by tags, source and date.");

        return app;
    }

    private static async Task<IResult> QueryAsync(
        QueryRequest request,
        CachedQuestionAnswering answering,
        HttpResponse response,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            errors["question"] = ["A question is required."];
        }
        else if (request.Question.Length > MaxQuestionLength)
        {
            errors["question"] = [$"The question must be at most {MaxQuestionLength} characters."];
        }

        if (request.TopK is < 1 or > MaxTopK)
        {
            errors["topK"] = [$"topK must be between 1 and {MaxTopK}."];
        }

        var filter = MetadataInput.ToSearchFilter(tenant.TenantId, request.Filters, errors);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var answer = await answering.AnswerAsync(request.Question!, request.TopK, filter, cancellationToken);
        response.Headers[CacheHeader] = answer.FromCache ? "hit" : "miss";
        return Results.Ok(answer.Result);
    }
}
