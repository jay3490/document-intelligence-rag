using System.Threading.Channels;
using DocumentIntelligence.Core.Documents;
using DocumentIntelligence.Core.Ingestion;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Api.Ingestion;

public sealed record IngestionWorkItem(Guid JobId, SourceDocument Document);

/// <summary>
/// A bounded hand-off between upload requests and the ingestion worker. Requests never wait
/// for space: when the queue is full the upload is refused so the client can retry later.
/// </summary>
public sealed class IngestionQueue
{
    private readonly Channel<IngestionWorkItem> _channel;

    public IngestionQueue(IOptions<IngestionOptions> options)
    {
        _channel = Channel.CreateBounded<IngestionWorkItem>(new BoundedChannelOptions(options.Value.QueueCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public bool TryEnqueue(IngestionWorkItem item) => _channel.Writer.TryWrite(item);

    public IAsyncEnumerable<IngestionWorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
