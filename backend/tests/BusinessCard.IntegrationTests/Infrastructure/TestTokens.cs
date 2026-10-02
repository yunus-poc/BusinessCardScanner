using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BusinessCard.IntegrationTests.Infrastructure;

/// <summary>
/// Issues Entra-shaped access tokens signed with a local key, so the real JwtBearer pipeline
/// (lifetime, audience, signature, scope) is exercised without calling Entra ID.
/// </summary>
public static class TestTokens
{
    public const string TenantId = "11111111-1111-1111-1111-111111111111";
    public const string ClientId = "22222222-2222-2222-2222-222222222222";
    public const string UserObjectId = "33333333-3333-3333-3333-333333333333";
    public const string ApiScope = "access_as_user";

    public static readonly string Issuer = $"https://login.microsoftonline.com/{TenantId}/v2.0";
    public static readonly string IssuerV1 = $"https://sts.windows.net/{TenantId}/";

    public static readonly SymmetricSecurityKey SigningKey = new(RandomNumberGenerator.GetBytes(32)) { KeyId = "test-key" };

    public static string Create(
        string? scope = ApiScope,
        string audience = ClientId,
        string? issuer = null,
        DateTime? expires = null,
        SecurityKey? signingKey = null,
        string version = "2.0")
    {
        var expiry = expires ?? DateTime.UtcNow.AddMinutes(30);
        var claims = new Dictionary<string, object>
        {
            ["oid"] = UserObjectId,
            ["tid"] = TenantId,
            ["ver"] = version,
        };
        if (scope is not null)
        {
            claims["scp"] = scope;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Issuer,
            Audience = audience,
            IssuedAt = expiry.AddHours(-1),
            NotBefore = expiry.AddHours(-1),
            Expires = expiry,
            Claims = claims,
            SigningCredentials = new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.HmacSha256),
        });
    }
}
