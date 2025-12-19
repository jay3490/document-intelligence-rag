using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocumentIntelligence.Infrastructure.Messaging.Kafka;

/// <summary>A transport-level record: key, JSON value and string headers.</summary>
public sealed record KafkaRecord(string Key, byte[] Value, IReadOnlyDictionary<string, string> Headers);

/// <summary>Writes records to a topic; a seam over the Confluent producer.</summary>
public interface IMessageProducer
{
    Task ProduceAsync(string topic, KafkaRecord record, CancellationToken cancellationToken);
}

/// <summary>Header names shared by producers and consumers.</summary>
public static class MessageHeaders
{
    public const string MessageType = "message-type";
    public const string CorrelationId = "correlation-id";
    public const string TraceParent = "traceparent";
    public const string Error = "error";
    public const string Attempts = "attempts";
}

/// <summary>JSON (de)serialisation of message payloads, plus correlation and trace headers.</summary>
public static class KafkaSerializer
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Builds a record. The current activity is written as a W3C <c>traceparent</c> header so the
    /// consumer's span joins the producer's trace.
    /// </summary>
    public static KafkaRecord ToRecord<T>(string key, T payload, string? correlationId, IReadOnlyDictionary<string, string>? extraHeaders = null)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MessageHeaders.MessageType] = typeof(T).Name,
        };

        if (!string.IsNullOrEmpty(correlationId))
        {
            headers[MessageHeaders.CorrelationId] = correlationId;
        }

        if (Activity.Current is { } activity)
        {
            headers[MessageHeaders.TraceParent] = activity.Id!;
        }

        foreach (var (name, value) in extraHeaders ?? new Dictionary<string, string>())
        {
            headers[name] = value;
        }

        return new KafkaRecord(key, JsonSerializer.SerializeToUtf8Bytes(payload, Json), headers);
    }

    /// <exception cref="JsonException">The value is not a valid <typeparamref name="T"/>.</exception>
    public static T Deserialize<T>(byte[] value) =>
        JsonSerializer.Deserialize<T>(value, Json) ?? throw new JsonException($"The message body is not a {typeof(T).Name}.");

    public static Dictionary<string, string> ReadHeaders(Confluent.Kafka.Headers? headers)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var header in headers ?? [])
        {
            result[header.Key] = Encoding.UTF8.GetString(header.GetValueBytes());
        }

        return result;
    }

    public static Confluent.Kafka.Headers WriteHeaders(IReadOnlyDictionary<string, string> headers)
    {
        var result = new Confluent.Kafka.Headers();
        foreach (var (name, value) in headers)
        {
            result.Add(name, Encoding.UTF8.GetBytes(value));
        }

        return result;
    }
}
