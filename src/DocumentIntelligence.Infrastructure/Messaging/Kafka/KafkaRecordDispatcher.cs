using System.Diagnostics;
using System.Text.Json;
using DocumentIntelligence.Core;
using Microsoft.Extensions.Logging;

namespace DocumentIntelligence.Infrastructure.Messaging.Kafka;

public enum DispatchResult
{
    /// <summary>The handler finished; the offset can be committed.</summary>
    Handled,

    /// <summary>The record could not be read and was parked; the offset can be committed.</summary>
    Poisoned,

    /// <summary>The handler threw; the record must be delivered again.</summary>
    Failed,
}

/// <summary>
/// Turns one consumed record into a handler call: deserialises it, continues the producer's
/// trace from the <c>traceparent</c> header, scopes logs with the correlation id and decides
/// whether the offset may be committed. Unreadable records go to the dead-letter topic rather
/// than blocking their partition.
/// </summary>
public sealed partial class KafkaRecordDispatcher<TMessage>(
    Func<TMessage, CancellationToken, Task> handler,
    IMessageProducer deadLetterProducer,
    string deadLetterTopic,
    ILogger logger)
{
    public async Task<DispatchResult> DispatchAsync(string topic, KafkaRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        record.Headers.TryGetValue(MessageHeaders.CorrelationId, out var correlationId);
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId, ["Topic"] = topic });

        ActivityContext.TryParse(record.Headers.GetValueOrDefault(MessageHeaders.TraceParent), null, out var parent);
        using var activity = Telemetry.ActivitySource.StartActivity($"{topic} process", ActivityKind.Consumer, parent);
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", topic);
        activity?.SetTag("correlation.id", correlationId);

        TMessage message;
        try
        {
            message = KafkaSerializer.Deserialize<TMessage>(record.Value);
        }
        catch (JsonException exception)
        {
            LogPoisoned(logger, exception, topic, record.Key);
            var headers = new Dictionary<string, string>(record.Headers, StringComparer.Ordinal)
            {
                [MessageHeaders.Error] = $"Unreadable {typeof(TMessage).Name}: {exception.Message}",
            };
            await deadLetterProducer.ProduceAsync(deadLetterTopic, record with { Headers = headers }, cancellationToken);
            return DispatchResult.Poisoned;
        }

        try
        {
            await handler(message, cancellationToken);
            return DispatchResult.Handled;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            LogHandlerFailed(logger, exception, topic, record.Key);
            return DispatchResult.Failed;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unreadable record on {Topic} (key {Key}) was moved to the dead-letter topic")]
    private static partial void LogPoisoned(ILogger logger, Exception exception, string topic, string key);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handling a record from {Topic} (key {Key}) failed; it will be redelivered")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, string topic, string key);
}
