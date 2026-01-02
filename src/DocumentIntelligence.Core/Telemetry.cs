using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DocumentIntelligence.Core;

/// <summary>
/// The activity source for spans emitted by ingestion, retrieval and answering, and the meter
/// for the service's own metrics. Metric names follow OpenTelemetry conventions; the Prometheus
/// exporter turns them into, for example, <c>di_query_duration_seconds</c>.
/// </summary>
public static class Telemetry
{
    public const string SourceName = "DocumentIntelligence";
    public const string MeterName = "DocumentIntelligence";

    public static readonly ActivitySource ActivitySource = new(SourceName);

    public static readonly Meter Meter = new(MeterName);

    /// <summary>Seconds from upload to the start of processing, which is the backlog an upload waited in.</summary>
    public static readonly Histogram<double> IngestionLag = Meter.CreateHistogram<double>(
        "di.ingestion.lag", unit: "s", description: "Time from upload until ingestion started.");

    /// <summary>Chunks written to the vector store; its rate is the ingestion throughput in chunks per second.</summary>
    public static readonly Counter<long> IngestedChunks = Meter.CreateCounter<long>(
        "di.ingestion.chunks", unit: "{chunk}", description: "Chunks embedded and written to the vector store.");

    /// <summary>Uploads handled, tagged with <c>outcome</c> (completed, duplicate, already_processed, dead_lettered).</summary>
    public static readonly Counter<long> IngestedDocuments = Meter.CreateCounter<long>(
        "di.ingestion.documents", unit: "{document}", description: "Uploads handled by outcome.");

    /// <summary>End-to-end question answering time, tagged with <c>cache</c> (hit or miss).</summary>
    public static readonly Histogram<double> QueryDuration = Meter.CreateHistogram<double>(
        "di.query.duration", unit: "s", description: "Time to answer a question.");

    /// <summary>Query cache lookups tagged with <c>result</c> (hit or miss); hits / total is the hit ratio.</summary>
    public static readonly Counter<long> QueryCacheLookups = Meter.CreateCounter<long>(
        "di.query.cache.lookups", unit: "{lookup}", description: "Query cache lookups by result.");
}
