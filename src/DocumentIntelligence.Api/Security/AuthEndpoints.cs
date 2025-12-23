using DocumentIntelligence.Core.Tenancy;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Api.Security;

public sealed record DevTokenRequest(string? Tenant, string? User);

public static class AuthEndpoints
{
    public const int MaxUserLength = 64;

    /// <summary>
    /// <c>POST /api/auth/dev-token</c> issues a signed token for any tenant. It is only mapped in
    /// DevToken mode; in Jwt mode tokens come from the configured identity provider.
    /// </summary>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.ServiceProvider.GetRequiredService<IOptions<AuthOptions>>().Value;
        if (auth.Mode != AuthMode.DevToken)
        {
            return app;
        }

        app.MapPost("/api/auth/dev-token", (DevTokenRequest request, DevTokenIssuer issuer) =>
            {
                var tenant = request.Tenant?.Trim();
                var user = string.IsNullOrWhiteSpace(request.User) ? "developer" : request.User.Trim();
                var errors = new Dictionary<string, string[]>();
                if (!TenantId.IsValid(tenant))
                {
                    errors["tenant"] = ["The tenant must be 1 to 64 lower-case letters, digits, '-' or '_', starting with a letter or digit."];
                }

                if (user.Length > MaxUserLength)
                {
                    errors["user"] = [$"The user name must be at most {MaxUserLength} characters."];
                }

                return errors.Count > 0 ? Results.ValidationProblem(errors) : Results.Ok(issuer.Issue(tenant!, user));
            })
            .AllowAnonymous()
            .WithTags("Auth")
            .WithSummary("Issue a development token for a tenant (DevToken mode only).");

        return app;
    }
}
