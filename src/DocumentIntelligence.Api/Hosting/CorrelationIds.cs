using System.Diagnostics;
using System.Text.RegularExpressions;
using Serilog.Context;

namespace DocumentIntelligence.Api.Hosting;

/// <summary>
/// Gives every request a correlation id: the caller's <c>X-Correlation-ID</c> when it is
/// well-formed, otherwise the W3C trace id. The id becomes the request's TraceIdentifier, is
/// echoed in the response, tagged on the request span and attached to every log event. Uploads
/// copy it into the DocumentUploaded message, so the worker's logs and spans carry it too.
/// </summary>
public static partial class CorrelationIds
{
    public const string HeaderName = "X-Correlation-ID";

    public static IApplicationBuilder UseCorrelationIds(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var correlationId = Resolve(context.Request.Headers[HeaderName].ToString(), Activity.Current);
            context.TraceIdentifier = correlationId;
            context.Response.Headers[HeaderName] = correlationId;
            Activity.Current?.SetTag("correlation.id", correlationId);

            using (LogContext.PushProperty("CorrelationId", correlationId))
            {
                await next(context);
            }
        });

    /// <summary>Accepts up to 128 letters, digits, '-', '_', '.' and ':'; anything else is replaced.</summary>
    public static string Resolve(string? supplied, Activity? activity) =>
        !string.IsNullOrEmpty(supplied) && Pattern().IsMatch(supplied)
            ? supplied
            : activity?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

    [GeneratedRegex("^[A-Za-z0-9_.:-]{1,128}$")]
    private static partial Regex Pattern();
}
