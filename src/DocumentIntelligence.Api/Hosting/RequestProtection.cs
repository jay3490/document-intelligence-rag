using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.RateLimiting;
using DocumentIntelligence.Api.Security;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Api.Hosting;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;

    /// <summary>Requests per tenant per minute across the API.</summary>
    [Range(1, 100_000)]
    public int RequestsPerMinute { get; set; } = 300;

    /// <summary>Questions per tenant per minute; each one may call the embedding and chat models.</summary>
    [Range(1, 100_000)]
    public int QueriesPerMinute { get; set; } = 60;

    /// <summary>Uploads per tenant per minute.</summary>
    [Range(1, 100_000)]
    public int UploadsPerMinute { get; set; } = 30;
}

public sealed class RequestLimitOptions
{
    public const string SectionName = "RequestLimits";

    /// <summary>Largest request body for everything except uploads, which follow Ingestion:MaxDocumentBytes.</summary>
    [Range(1024, 100 * 1024 * 1024)]
    public long MaxRequestBodyBytes { get; set; } = 64 * 1024;
}

public sealed class CorsSettings
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];

    public string[] AllowedMethods { get; set; } = ["GET", "POST", "DELETE"];

    public string[] AllowedHeaders { get; set; } = ["Authorization", "Content-Type", "X-Correlation-ID"];

    public string[] ExposedHeaders { get; set; } = ["X-Cache", "X-Correlation-ID", "Retry-After", "Location"];

    [Range(0, 86_400)]
    public int PreflightMaxAgeSeconds { get; set; } = 600;
}

/// <summary>Rate limiting, request size limits and CORS for the API.</summary>
public static class RequestProtection
{
    public const string QueryPolicy = "queries";
    public const string UploadPolicy = "uploads";

    /// <summary>Multipart framing allowed on top of the document itself.</summary>
    public const long UploadOverheadBytes = 64 * 1024;

    public static WebApplicationBuilder AddRequestProtection(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddOptions<RateLimitingOptions>().Bind(builder.Configuration.GetSection(RateLimitingOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<RequestLimitOptions>().Bind(builder.Configuration.GetSection(RequestLimitOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<CorsSettings>().Bind(builder.Configuration.GetSection(CorsSettings.SectionName)).ValidateDataAnnotations().ValidateOnStart();

        // Kestrel enforces the general limit; the upload endpoint raises it for its own requests.
        builder.WebHost.ConfigureKestrel((context, kestrel) =>
            kestrel.Limits.MaxRequestBodySize = context.Configuration.GetSection(RequestLimitOptions.SectionName).Get<RequestLimitOptions>()?.MaxRequestBodyBytes
                ?? new RequestLimitOptions().MaxRequestBodyBytes);

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = OnRejectedAsync;
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                PerTenant(context, settings => settings.RequestsPerMinute, "all"));
            limiter.AddPolicy(QueryPolicy, context => PerTenant(context, settings => settings.QueriesPerMinute, QueryPolicy));
            limiter.AddPolicy(UploadPolicy, context => PerTenant(context, settings => settings.UploadsPerMinute, UploadPolicy));
        });

        services.AddCors();
        return builder;
    }

    public static WebApplication UseRequestProtection(this WebApplication app)
    {
        app.Use(EnforceBodySizeAsync);

        var cors = app.Services.GetRequiredService<IOptions<CorsSettings>>().Value;
        app.UseCors(policy => policy
            .WithOrigins(cors.AllowedOrigins)
            .WithMethods(cors.AllowedMethods)
            .WithHeaders(cors.AllowedHeaders)
            .WithExposedHeaders(cors.ExposedHeaders)
            .SetPreflightMaxAge(TimeSpan.FromSeconds(cors.PreflightMaxAgeSeconds)));
        return app;
    }

    /// <summary>
    /// Applies the endpoint's own body limit (<see cref="BodySizeLimit"/>) or the general one:
    /// a declared Content-Length above it is answered with 413 straight away, and the server's
    /// per-request limit is set so chunked bodies are cut off too.
    /// </summary>
    private static Task EnforceBodySizeAsync(HttpContext context, RequestDelegate next)
    {
        var limit = context.GetEndpoint()?.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize
            ?? context.RequestServices.GetRequiredService<IOptions<RequestLimitOptions>>().Value.MaxRequestBodyBytes;

        if (context.Request.ContentLength > limit)
        {
            return Results.Problem(
                    statusCode: StatusCodes.Status413PayloadTooLarge,
                    title: "Request body too large",
                    detail: $"This endpoint accepts at most {limit} bytes.")
                .ExecuteAsync(context);
        }

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
        {
            feature.MaxRequestBodySize = limit;
        }

        return next(context);
    }

    /// <summary>
    /// A fixed one-minute window per tenant (from the token) or, for anonymous callers, per
    /// client address. Health probes are never limited.
    /// </summary>
    private static RateLimitPartition<string> PerTenant(HttpContext context, Func<RateLimitingOptions, int> permits, string bucket)
    {
        var settings = context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
        if (!settings.Enabled || context.Request.Path.StartsWithSegments("/health"))
        {
            return RateLimitPartition.GetNoLimiter($"{bucket}:unlimited");
        }

        var tenantClaim = context.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value.TenantClaim;
        var caller = context.User.FindFirst(tenantClaim)?.Value is { Length: > 0 } tenant
            ? $"tenant:{tenant}"
            : $"ip:{context.Connection.RemoteIpAddress}";

        return RateLimitPartition.GetFixedWindowLimiter($"{bucket}:{caller}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits(settings),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    }

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        await Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Too many requests",
                detail: "The rate limit for this tenant was reached. Retry after the time in the Retry-After header.")
            .ExecuteAsync(http);
    }
}

/// <summary>Endpoint metadata that sets a body size limit different from the general one.</summary>
public sealed record BodySizeLimit(long? MaxRequestBodySize) : IRequestSizeLimitMetadata;
