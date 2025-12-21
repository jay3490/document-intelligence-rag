using DocumentIntelligence.Functions;
using Microsoft.Extensions.Hosting;

// Isolated-worker host for the drop-zone functions: a blob trigger that publishes dropped files
// to Kafka, and an HTTP function that reports ingestion status from the job database.
var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) => services.AddDropFunctions(context.Configuration))
    .Build();

await host.RunAsync();
