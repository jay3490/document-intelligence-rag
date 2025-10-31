using System.Diagnostics;

namespace DocumentIntelligence.Core;

/// <summary>The activity source for spans emitted by ingestion, retrieval and answering.</summary>
public static class Telemetry
{
    public const string SourceName = "DocumentIntelligence";

    public static readonly ActivitySource ActivitySource = new(SourceName);
}
