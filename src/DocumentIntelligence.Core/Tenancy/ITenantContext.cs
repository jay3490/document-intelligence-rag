namespace DocumentIntelligence.Core.Tenancy;

/// <summary>The tenant the current request or message is acting for.</summary>
public interface ITenantContext
{
    string TenantId { get; }
}
