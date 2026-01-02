using System.Text.Json;
using DocumentIntelligence.Core;
using DocumentIntelligence.IngestionWorker;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

// The ingestion worker consumes DocumentUploaded messages from Kafka, then chunks, embeds and
// upserts each document into the vector store. It serves no API; its small HTTP surface exists
// for health probes and Prometheus metrics (ingestion lag, chunks written, outcomes).
const string ServiceName = "document-intelligence-ingestion-worker";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", ServiceName));

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(ServiceName))
    .WithTracing(tracing =>
    {
        tracing.AddSource(Telemetry.SourceName).AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            tracing.AddOtlpExporter();
        }
    })
    .WithMetrics(metrics => metrics
        .AddMeter(Telemetry.MeterName)
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddPrometheusExporter());

builder.Services.AddIngestionWorker(builder.Configuration);

var app = builder.Build();

app.MapPrometheusScrapingEndpoint("/metrics");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = WriteHealthAsync });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(IngestionWorkerServices.ReadyTag), ResponseWriter = WriteHealthAsync });

app.Run();

static Task WriteHealthAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    return context.Response.WriteAsync(JsonSerializer.Serialize(
        new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new { name = entry.Key, status = entry.Value.Status.ToString(), description = entry.Value.Description }),
        },
        new JsonSerializerOptions(JsonSerializerDefaults.Web)));
}
