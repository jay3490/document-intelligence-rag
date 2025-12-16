using DocumentIntelligence.Core.Ingestion;
using DocumentIntelligence.Core.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DocumentIntelligence.Infrastructure.Messaging;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the upload handler and the publisher for the configured
    /// <see cref="IngestionOptions.Transport"/>.
    /// </summary>
    /// <param name="consumeInProcess">
    /// Also run the in-process consumer when the transport is <see cref="IngestionTransport.InProcess"/>.
    /// </param>
    public static IServiceCollection AddIngestionMessaging(this IServiceCollection services, IConfiguration configuration, bool consumeInProcess)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<DocumentUploadedHandler>();

        var transport = configuration.GetSection(IngestionOptions.SectionName).GetValue<IngestionTransport?>(nameof(IngestionOptions.Transport))
            ?? IngestionTransport.InProcess;

        switch (transport)
        {
            case IngestionTransport.InProcess:
                services.AddSingleton<InProcessIngestionChannel>();
                services.AddSingleton<IIngestionEventPublisher, InProcessIngestionEventPublisher>();
                if (consumeInProcess)
                {
                    services.AddHostedService<InProcessIngestionConsumer>();
                }

                break;
            default:
                throw new InvalidOperationException($"Unsupported ingestion transport '{transport}'.");
        }

        return services;
    }
}
