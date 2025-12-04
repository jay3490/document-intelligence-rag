using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace DocumentIntelligence.Core.Tenancy;

/// <summary>
/// Tenant identifiers. Every chunk, ingestion job and query belongs to exactly one tenant, and
/// retrieval never crosses tenants.
/// </summary>
public static partial class TenantId
{
    /// <summary>The tenant used when the deployment does not distinguish tenants.</summary>
    public const string Default = "default";

    public const int MaxLength = 64;

    /// <summary>Lower-case letters, digits, '-' and '_', starting with a letter or digit.</summary>
    public static bool IsValid([NotNullWhen(true)] string? value) =>
        value is not null && Pattern().IsMatch(value);

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,63}$")]
    private static partial Regex Pattern();
}
