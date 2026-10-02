using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;

namespace BusinessCard.Api.Authentication;

internal static class AuthenticationSetup
{
    public const string SectionName = "AzureAd";

    private static readonly string[] RequiredKeys = ["Instance", "TenantId", "ClientId", "Scopes"];

    /// <summary>
    /// Validates Entra ID access tokens (signature, issuer, audience, lifetime) and requires the
    /// delegated API scope on every endpoint unless it is explicitly marked [AllowAnonymous].
    /// </summary>
    public static IServiceCollection AddEntraAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var requiredScopes = RequireConfiguration(section);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(section)
            // On-Behalf-Of: exchanges the user's API token for a delegated Graph token. Needs the API's
            // client credential (AzureAd:ClientCredentials) at runtime, from a secret store.
            .EnableTokenAcquisitionToCallDownstreamApi()
            // Per-instance cache of OBO tokens; entries expire with the tokens. A distributed cache is
            // only worth it when many API instances serve the same users.
            .AddInMemoryTokenCaches();

        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            // Default skew is 5 minutes; 1 minute tolerates normal clock drift without
            // extending the life of an expired token.
            options.TokenValidationParameters.ClockSkew = TimeSpan.FromMinutes(1);
            options.TokenValidationParameters.RequireExpirationTime = true;
            options.TokenValidationParameters.ValidateLifetime = true;
        });

        var apiUserPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireScope(requiredScopes)
            .Build();

        services.AddAuthorizationBuilder()
            // Default: a plain [Authorize] must not silently drop the scope requirement.
            .SetDefaultPolicy(apiUserPolicy)
            // Fallback: deny by default, so an endpoint someone forgets to annotate is still protected.
            .SetFallbackPolicy(apiUserPolicy);

        return services;
    }

    /// <summary>Fails at startup with an actionable message instead of a cryptic 401 on first request.</summary>
    private static string[] RequireConfiguration(IConfigurationSection section)
    {
        var missing = RequiredKeys
            .Where(key => string.IsNullOrWhiteSpace(section[key]))
            .Select(key => $"{SectionName}:{key}")
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Missing Entra ID configuration: {string.Join(", ", missing)}. See README → 'Entra ID / App Registration'.");
        }

        return section["Scopes"]!.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
