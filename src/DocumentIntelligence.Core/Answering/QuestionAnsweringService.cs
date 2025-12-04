using DocumentIntelligence.Core.Chat;
using DocumentIntelligence.Core.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Core.Answering;

/// <summary>Answers a question from the ingested documents and reports which chunks it cited.</summary>
public sealed partial class QuestionAnsweringService(
    HybridRetriever retriever,
    IChatCompletionClient chatClient,
    IOptions<RetrievalOptions> options,
    ILogger<QuestionAnsweringService> logger)
{
    public const string NothingIndexedAnswer =
        "There is no ingested content to answer from yet. Upload a document and try again.";

    public async Task<AnswerResult> AnswerAsync(string question, int? topK, SearchFilter filter, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(filter);

        using var activity = Telemetry.ActivitySource.StartActivity("answer question");

        var sources = await retriever.RetrieveAsync(question, topK ?? options.Value.DefaultTopK, filter, cancellationToken);
        if (sources.Count == 0)
        {
            return new AnswerResult(NothingIndexedAnswer, []);
        }

        string answer;
        using (Telemetry.ActivitySource.StartActivity("generate answer"))
        {
            answer = (await chatClient.CompleteAsync(GroundedPrompt.Build(question, sources), cancellationToken)).Trim();
        }

        var citations = CitationParser.Parse(answer, sources.Count)
            .Select(number =>
            {
                var source = sources[number - 1];
                var chunk = source.Chunk;
                return new Citation(number, chunk.Id, chunk.DocumentId, chunk.DocumentName, chunk.Section, chunk.ChunkIndex, chunk.Text, source.Score);
            })
            .ToList();

        activity?.SetTag("answer.sources", sources.Count);
        activity?.SetTag("answer.citations", citations.Count);
        LogAnswered(logger, sources.Count, citations.Count);

        return new AnswerResult(answer, citations);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Answered a question from {Sources} sources with {Citations} citations")]
    private static partial void LogAnswered(ILogger logger, int sources, int citations);
}
