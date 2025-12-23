using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DocumentIntelligence.Api.Security;

public sealed record DevToken(string AccessToken, DateTimeOffset ExpiresAt, string TenantId, string Subject);

/// <summary>Signs development tokens that the API's bearer authentication accepts in DevToken mode.</summary>
public sealed class DevTokenIssuer(IOptions<AuthOptions> options, TimeProvider timeProvider)
{
    public DevToken Issue(string tenantId, string subject)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var expires = now.AddMinutes(settings.DevToken.LifetimeMinutes);

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.DevToken.Issuer,
            Audience = settings.DevToken.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", subject),
                new Claim("name", subject),
                new Claim(settings.TenantClaim, tenantId),
            ]),
            SigningCredentials = new SigningCredentials(SigningKey(settings.DevToken), SecurityAlgorithms.HmacSha256),
        });

        return new DevToken(token, expires, tenantId, subject);
    }

    public static SymmetricSecurityKey SigningKey(DevTokenOptions options) =>
        new(Encoding.UTF8.GetBytes(options.SigningKey ?? throw new InvalidOperationException("Auth:DevToken:SigningKey is not set.")));
}
