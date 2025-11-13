using DocumentIntelligence.Core.Answering;

namespace DocumentIntelligence.Api.Endpoints;

public sealed record QueryRequest(string? Question, int? TopK);

public static class QueryEndpoints
{
    public const int MaxQuestionLength = 2000;
    public const int MaxTopK = 20;

    public static IEndpointRouteBuilder MapQueryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/query", QueryAsync)
            .WithTags("Query")
            .WithSummary("Answer a question from the ingested documents, with citations.");

        return app;
    }

    private static async Task<IResult> QueryAsync(
        QueryRequest request,
        QuestionAnsweringService answering,
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

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await answering.AnswerAsync(request.Question!, request.TopK, cancellationToken);
        return Results.Ok(result);
    }
}
