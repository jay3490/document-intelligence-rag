using DocumentIntelligence.Core;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace DocumentIntelligence.Api.Hosting;

public static class ObservabilityExtensions
{
    public const string ServiceName = "document-intelligence-api";
    public const string MetricsPath = "/metrics";

    /// <summary>
    /// Serilog, configured from the "Serilog" section, replaces the default logging providers.
    /// Every request's correlation id is attached to its log events.
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
    /// ingestion, retrieval and answering spans, exported over OTLP when the standard
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> setting is present. Metrics (the service's own meter,
    /// ASP.NET Core, HTTP client and runtime) are exposed for Prometheus at <c>/metrics</c>.
    /// </summary>
    public static WebApplicationBuilder AddTelemetry(this WebApplicationBuilder builder) =>
        builder.AddTelemetry(ServiceName);

    public static WebApplicationBuilder AddTelemetry(this WebApplicationBuilder builder, string serviceName)
    {
        var exportOtlp = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(Telemetry.SourceName)
                    .AddAspNetCoreInstrumentation(options =>
                        options.Filter = context =>
                            !context.Request.Path.StartsWithSegments("/health") && !context.Request.Path.StartsWithSegments(MetricsPath))
                    .AddHttpClientInstrumentation();

                if (exportOtlp)
                {
                    tracing.AddOtlpExporter();
                }
            })
            .WithMetrics(metrics => metrics
                .AddMeter(Telemetry.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter());

        return builder;
    }

    /// <summary>The Prometheus scrape endpoint; anonymous so the scraper needs no token.</summary>
    public static IEndpointRouteBuilder MapMetrics(this IEndpointRouteBuilder app)
    {
        app.MapPrometheusScrapingEndpoint(MetricsPath).AllowAnonymous();
        return app;
    }
}
