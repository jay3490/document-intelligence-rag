using DocumentIntelligence.Core.Ingestion;
using DocumentIntelligence.Core.Messaging;
using DocumentIntelligence.Core.Search;
using DocumentIntelligence.IngestionWorker;
using DocumentIntelligence.Infrastructure.Messaging.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocumentIntelligence.UnitTests.Worker;

public sealed class IngestionWorkerServicesTests
{
    [Fact]
    public void The_worker_service_graph_resolves_without_a_keyword_index()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ingestion:Transport"] = "Kafka",
                ["Persistence:Provider"] = "Sqlite",
                ["Persistence:ConnectionString"] = "Data Source=:memory:",
                ["BlobStorage:FileSystem:RootPath"] = Path.GetTempPath(),
            })
            .Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddIngestionWorker(configuration);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.IsType<NoKeywordIndexWriter>(provider.GetRequiredService<IKeywordIndexWriter>());
        Assert.IsType<KafkaIngestionEventPublisher>(provider.GetRequiredService<IIngestionEventPublisher>());
        Assert.NotNull(provider.GetRequiredService<DocumentUploadedHandler>());
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(DocumentUploadedKafkaConsumer));
    }
}
