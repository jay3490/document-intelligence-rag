using DocumentIntelligence.Core;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace DocumentIntelligence.Api.Hosting;

public static class ObservabilityExtensions
{
    public const string ServiceName = "document-intelligence-api";

    /// <summary>
    /// Serilog, configured from the "Serilog" section, replaces the default logging providers.
    /// </summary>
    public static WebApplicationBuilder AddStructuredLogging(this WebApplicationBuilder builder)
    {
        builder.Services.AddSerilog((services, logger) => logger
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", ServiceName));

        return builder;
    }

    /// <summary>
    /// Traces incoming requests, outgoing HTTP calls (vector store, model endpoints) and the
    /// ingestion, retrieval and answering spans. Spans are exported over OTLP when the
    /// standard <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> setting is present.
    /// </summary>
    public static WebApplicationBuilder AddTracing(this WebApplicationBuilder builder)
    {
        var exportOtlp = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(Telemetry.SourceName)
                    .AddAspNetCoreInstrumentation(options =>
                        options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                    .AddHttpClientInstrumentation();

                if (exportOtlp)
                {
                    tracing.AddOtlpExporter();
                }
            });

        return builder;
    }
}
