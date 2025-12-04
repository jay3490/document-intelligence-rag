using DocumentIntelligence.Core.Tenancy;

namespace DocumentIntelligence.Api.Tenancy;

/// <summary>Places every request in the default tenant.</summary>
public sealed class DefaultTenantContext : ITenantContext
{
    public string TenantId => Core.Tenancy.TenantId.Default;
}
