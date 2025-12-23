using DocumentIntelligence.Core.Tenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DocumentIntelligence.Api.Security;

public static class AuthenticationSetup
{
    /// <summary>Requires an authenticated caller with a valid tenant claim; the default for every endpoint.</summary>
    public const string TenantPolicy = "tenant";

    /// <summary>
    /// JWT bearer authentication in either mode, a fallback policy that demands a tenant claim
    /// on every endpoint not marked anonymous, and the claims-based tenant context.
    /// </summary>
    public static WebApplicationBuilder AddApiAuthentication(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddOptions<AuthOptions>()
            .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                auth => auth.Mode != AuthMode.DevToken || builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") || auth.DevToken.AllowOutsideDevelopment,
                "Auth:Mode = DevToken is refused outside Development unless Auth:DevToken:AllowOutsideDevelopment is true.")
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, ClaimsTenantContext>();
        services.AddSingleton<DevTokenIssuer>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthOptions>>((jwt, authOptions) => Configure(jwt, authOptions.Value));

        var tenantPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => HasValidTenant(context, builder.Configuration))
            .Build();
        services.AddAuthorizationBuilder()
            .AddPolicy(TenantPolicy, tenantPolicy)
            .SetFallbackPolicy(tenantPolicy);

        return builder;
    }

    private static void Configure(JwtBearerOptions jwt, AuthOptions auth)
    {
        // Keep claim names as issued ("sub", "tenant_id") rather than mapping to long URIs.
        jwt.MapInboundClaims = false;

        if (auth.Mode == AuthMode.Jwt)
        {
            jwt.Authority = auth.Authority;
            jwt.Audience = auth.Audience;
            jwt.RequireHttpsMetadata = auth.RequireHttpsMetadata;
            jwt.TokenValidationParameters.NameClaimType = "name";
            return;
        }

        jwt.RequireHttpsMetadata = false;
        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = auth.DevToken.Issuer,
            ValidAudience = auth.DevToken.Audience,
            IssuerSigningKey = DevTokenIssuer.SigningKey(auth.DevToken),
            NameClaimType = "name",
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    }

    private static bool HasValidTenant(AuthorizationHandlerContext context, IConfiguration configuration)
    {
        var claim = configuration[$"{AuthOptions.SectionName}:{nameof(AuthOptions.TenantClaim)}"] ?? new AuthOptions().TenantClaim;
        return TenantId.IsValid(context.User.FindFirst(claim)?.Value);
    }
}
