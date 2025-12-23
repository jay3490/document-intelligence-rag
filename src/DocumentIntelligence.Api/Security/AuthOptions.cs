using System.ComponentModel.DataAnnotations;
using System.Text;

namespace DocumentIntelligence.Api.Security;

public enum AuthMode
{
    /// <summary>Bearer tokens from an external OpenID Connect provider (Entra ID, Auth0, Keycloak, ...).</summary>
    Jwt,

    /// <summary>
    /// Tokens signed with a shared key and issued by <c>POST /api/auth/dev-token</c>, for local
    /// development and demos. Anyone who can reach that endpoint can act as any tenant.
    /// </summary>
    DevToken,
}

public sealed class AuthOptions : IValidatableObject
{
    public const string SectionName = "Auth";

    public AuthMode Mode { get; set; } = AuthMode.Jwt;

    /// <summary>The token issuer's base URL; its OpenID configuration supplies the signing keys.</summary>
    public string? Authority { get; set; }

    /// <summary>The audience tokens must be issued for.</summary>
    public string? Audience { get; set; }

    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>The claim that carries the caller's tenant id.</summary>
    [Required]
    public string TenantClaim { get; set; } = "tenant_id";

    public DevTokenOptions DevToken { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Mode == AuthMode.Jwt)
        {
            if (!Uri.TryCreate(Authority, UriKind.Absolute, out _))
            {
                yield return new ValidationResult("Authority must be an absolute URL in Jwt mode.", [nameof(Authority)]);
            }

            if (string.IsNullOrWhiteSpace(Audience))
            {
                yield return new ValidationResult("Audience is required in Jwt mode.", [nameof(Audience)]);
            }
        }
        else if (Encoding.UTF8.GetByteCount(DevToken.SigningKey ?? string.Empty) < DevTokenOptions.MinimumKeyBytes)
        {
            yield return new ValidationResult(
                $"DevToken:SigningKey must be at least {DevTokenOptions.MinimumKeyBytes} bytes.", ["DevToken.SigningKey"]);
        }
    }
}

public sealed class DevTokenOptions
{
    public const int MinimumKeyBytes = 32;

    /// <summary>HMAC-SHA256 key shared by the issuing endpoint and token validation.</summary>
    public string? SigningKey { get; set; }

    public string Issuer { get; set; } = "document-intelligence-dev";

    public string Audience { get; set; } = "document-intelligence";

    [Range(1, 7 * 24 * 60)]
    public int LifetimeMinutes { get; set; } = 480;

    /// <summary>
    /// Dev tokens are refused in the Production environment unless this is set, which the local
    /// docker compose stack does deliberately.
    /// </summary>
    public bool AllowOutsideDevelopment { get; set; }
}
