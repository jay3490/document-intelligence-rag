using System.Threading.Channels;
using DocumentIntelligence.Core.Ingestion;
using DocumentIntelligence.Core.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Infrastructure.Messaging;

/// <summary>A bounded hand-off between upload requests and the in-process consumer.</summary>
public sealed class InProcessIngestionChannel(IOptions<IngestionOptions> options)
{
    private readonly Channel<DocumentUploaded> _channel = Channel.CreateBounded<DocumentUploaded>(
        new BoundedChannelOptions(options.Value.QueueCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    public bool TryWrite(DocumentUploaded message) => _channel.Writer.TryWrite(message);

    public IAsyncEnumerable<DocumentUploaded> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>
/// Publishes uploads to the in-process channel. Requests never wait for space: a full channel
/// is reported as back-pressure. Dead letters are logged, and index changes need no message
/// because the in-process pipeline updates the local keyword index directly.
/// </summary>
public sealed partial class InProcessIngestionEventPublisher(
    InProcessIngestionChannel channel,
    ILogger<InProcessIngestionEventPublisher> logger) : IIngestionEventPublisher
{
    public Task PublishUploadedAsync(DocumentUploaded message, CancellationToken cancellationToken)
    {
        if (!channel.TryWrite(message))
        {
            throw new IngestionBackpressureException("Too many documents are waiting to be ingested. Try again shortly.");
        }

        return Task.CompletedTask;
    }

    public Task PublishDeadLetterAsync(DeadLetteredUpload message, CancellationToken cancellationToken)
    {
        LogDeadLetter(logger, message.Message.JobId, message.Message.BlobName, message.Attempts, message.Error);
        return Task.CompletedTask;
    }

    public Task PublishIndexChangedAsync(DocumentIndexChanged message, CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Error, Message = "Dead letter: job {JobId} ({BlobName}) failed after {Attempts} attempt(s): {Error}")]
    private static partial void LogDeadLetter(ILogger logger, Guid jobId, string blobName, int attempts, string error);
}

/// <summary>Drains the in-process channel one upload at a time once every readiness gate is open.</summary>
public sealed partial class InProcessIngestionConsumer(
    InProcessIngestionChannel channel,
    DocumentUploadedHandler handler,
    IEnumerable<IIngestionReadiness> readiness,
    ILogger<InProcessIngestionConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var gate in readiness)
        {
            await gate.WaitUntilReadyAsync(stoppingToken);
        }

        await foreach (var message in channel.ReadAllAsync(stoppingToken))
        {
            try
            {
                await handler.HandleAsync(message, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // The handler records failures itself; this only guards the loop, for example
                // against the job database being unreachable.
                LogUnhandled(logger, exception, message.JobId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Handling upload {JobId} failed")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, Guid jobId);
}
