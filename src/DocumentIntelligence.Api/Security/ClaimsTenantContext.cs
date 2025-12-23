using DocumentIntelligence.Core.Tenancy;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Api.Security;

/// <summary>
/// Takes the tenant from the authenticated caller's token. Requests only reach tenant-scoped
/// endpoints when the claim is present and valid (see <see cref="AuthenticationSetup.TenantPolicy"/>),
/// so every query, upload and listing is confined to that tenant.
/// </summary>
public sealed class ClaimsTenantContext(IHttpContextAccessor httpContextAccessor, IOptions<AuthOptions> options) : ITenantContext
{
    public string TenantId =>
        httpContextAccessor.HttpContext?.User.FindFirst(options.Value.TenantClaim)?.Value is { } tenant && Core.Tenancy.TenantId.IsValid(tenant)
            ? tenant
            : throw new InvalidOperationException("The request has no valid tenant claim.");
}
