using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using DocumentIntelligence.Core;
using DocumentIntelligence.Core.Answering;
using DocumentIntelligence.Core.Search;
using DocumentIntelligence.Core.Storage;
using DocumentIntelligence.Infrastructure.Caching;
using DocumentIntelligence.Infrastructure.Offline;
using DocumentIntelligence.Infrastructure.VectorStores;
using DocumentIntelligence.UnitTests.TestDoubles;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.UnitTests;

public sealed class TelemetryTests : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<(string Instrument, double Value, Dictionary<string, object?> Tags)> _measurements = new();

    public TelemetryTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == Telemetry.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.Start();
    }

    [Fact]
    public async Task Cached_answers_record_latency_and_hit_ratio_inputs()
    {
        // A tag that only this test uses keeps measurements from parallel tests apart.
        var tenant = $"t{Guid.NewGuid():N}"[..20];
        var answering = await CreateAnsweringAsync(tenant);
        var filter = new SearchFilter(tenant);

        await answering.AnswerAsync("When is lunch?", null, filter, CancellationToken.None);
        await answering.AnswerAsync("when is lunch?", null, filter, CancellationToken.None);

        var lookups = _measurements.Where(m => m.Instrument == "di.query.cache.lookups").Select(m => (string?)m.Tags["result"]).ToList();
        Assert.Contains("miss", lookups);
        Assert.Contains("hit", lookups);
        Assert.Contains(_measurements, m => m.Instrument == "di.query.duration" && (string?)m.Tags["cache"] == "hit" && m.Value >= 0);
    }

    [Fact]
    public void Instrument_names_are_stable()
    {
        Assert.Equal("di.ingestion.lag", Telemetry.IngestionLag.Name);
        Assert.Equal("s", Telemetry.IngestionLag.Unit);
        Assert.Equal("di.ingestion.chunks", Telemetry.IngestedChunks.Name);
        Assert.Equal("di.ingestion.documents", Telemetry.IngestedDocuments.Name);
        Assert.Equal("di.query.duration", Telemetry.QueryDuration.Name);
        Assert.Equal("di.query.cache.lookups", Telemetry.QueryCacheLookups.Name);
    }

    public void Dispose() => _listener.Dispose();

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = new Dictionary<string, object?>();
        foreach (var tag in tags)
        {
            copy[tag.Key] = tag.Value;
        }

        _measurements.Enqueue((instrument.Name, value, copy));
    }

    private static async Task<CachedQuestionAnswering> CreateAnsweringAsync(string tenant)
    {
        var embeddings = new HashingEmbeddingClient(32);
        var store = new InMemoryVectorStore(32);
        var keywords = new Bm25Index();
        var chunk = Chunks.Create("Lunch is at noon.") with { TenantId = tenant };
        var vectors = await embeddings.EmbedAsync([chunk.SearchText], CancellationToken.None);
        await store.UpsertAsync([new EmbeddedChunk(chunk, vectors[0])], CancellationToken.None);
        keywords.Add([chunk]);

        var options = Options.Create(new RetrievalOptions());
        var service = new QuestionAnsweringService(
            new HybridRetriever(embeddings, store, keywords, options, NullLogger<HybridRetriever>.Instance),
            new NoOpReranker(),
            new ExtractiveChatClient(),
            options,
            NullLogger<QuestionAnsweringService>.Instance);
        var cache = new DistributedQueryResultCache(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Options.Create(new QueryCacheOptions()),
            NullLogger<DistributedQueryResultCache>.Instance);
        return new CachedQuestionAnswering(service, cache, options);
    }
}
