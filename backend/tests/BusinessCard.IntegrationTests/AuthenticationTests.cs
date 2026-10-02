using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using BusinessCard.Application.Errors;
using BusinessCard.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace BusinessCard.IntegrationTests;

public sealed class AuthenticationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string FallbackEndpoint = "/test/auth/fallback";

    [Theory]
    [InlineData(FallbackEndpoint)]
    [InlineData("/test/auth/authorize-attribute")]
    public async Task ValidToken_IsAccepted(string path)
    {
        var response = await factory.CreateAuthenticatedClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task V1TokenWithAppIdUriAudience_IsAccepted()
    {
        // If the registration's accessTokenAcceptedVersion is left at v1, Entra issues
        // sts.windows.net tokens with "api://<client-id>" as audience. Both versions must work.
        var token = TestTokens.Create(audience: $"api://{TestTokens.ClientId}", issuer: TestTokens.IssuerV1, version: "1.0");

        var response = await factory.CreateAuthenticatedClient(token).GetAsync(FallbackEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MissingToken_Returns401ProblemWithBearerChallenge()
    {
        var response = await factory.CreateClient().GetAsync(FallbackEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
        Assert.Equal(ErrorCodes.Unauthenticated, await ReadCodeAsync(response));
    }

    public static TheoryData<string, string> InvalidTokens() => new()
    {
        { "expired", TestTokens.Create(expires: DateTime.UtcNow.AddMinutes(-5)) },
        { "wrong audience", TestTokens.Create(audience: "api://some-other-api") },
        { "v2 token with v1 audience", TestTokens.Create(audience: $"api://{TestTokens.ClientId}") },
        { "wrong issuer", TestTokens.Create(issuer: "https://login.microsoftonline.com/other-tenant/v2.0") },
        { "wrong signing key", TestTokens.Create(signingKey: new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32))) },
        { "no scope claim", TestTokens.Create(scope: null) },
        { "not a jwt", "not-a-token" },
    };

    [Theory]
    [MemberData(nameof(InvalidTokens))]
    public async Task InvalidToken_Returns401(string scenario, string token)
    {
        var response = await factory.CreateAuthenticatedClient(token).GetAsync(FallbackEndpoint);

        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{scenario}: got {response.StatusCode}");
        Assert.Equal(ErrorCodes.Unauthenticated, await ReadCodeAsync(response));
    }

    [Theory]
    [InlineData(FallbackEndpoint)]
    [InlineData("/test/auth/authorize-attribute")]
    public async Task TokenWithoutApiScope_Returns403(string path)
    {
        var token = TestTokens.Create(scope: "User.Read");

        var response = await factory.CreateAuthenticatedClient(token).GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ErrorCodes.Forbidden, await ReadCodeAsync(response));
    }

    [Fact]
    public async Task UnknownRoute_WithoutToken_Returns401NotRouteInformation()
    {
        var response = await factory.CreateClient().GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoints_StayAnonymous()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void MissingEntraConfiguration_FailsAtStartupWithActionableMessage()
    {
        using var misconfigured = factory.WithWebHostBuilder(builder => builder.UseSetting("AzureAd:ClientId", ""));

        var exception = Assert.Throws<InvalidOperationException>(() => misconfigured.CreateClient());
        Assert.Contains("AzureAd:ClientId", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return body.GetProperty("code").GetString();
    }
}
