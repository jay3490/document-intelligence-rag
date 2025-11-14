using DocumentIntelligence.Core;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DocumentIntelligence.Api.Hosting;

/// <summary>
/// Turns failures of the vector store or the model endpoints into a 502 problem response that
/// names the dependency, instead of a generic 500.
/// </summary>
public sealed partial class UpstreamServiceExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<UpstreamServiceExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not UpstreamServiceException upstream)
        {
            return false;
        }

        LogUpstreamFailure(logger, upstream, upstream.Service);

        httpContext.Response.StatusCode = StatusCodes.Status502BadGateway;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway,
                Title = "A dependency failed",
                Detail = upstream.Message,
                Extensions = { ["service"] = upstream.Service },
            },
        });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Request failed because {Service} failed")]
    private static partial void LogUpstreamFailure(ILogger logger, Exception exception, string service);
}
