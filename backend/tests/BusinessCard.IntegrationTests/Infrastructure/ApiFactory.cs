using System.Net.Http.Headers;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace BusinessCard.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real API in memory. Runs in the "Testing" environment so a developer's local
/// appsettings.Development.json never influences test results.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        builder.UseSetting("AzureAd:TenantId", TestTokens.TenantId);
        builder.UseSetting("AzureAd:ClientId", TestTokens.ClientId);
        // Automated tests never call a real model; tests that need one swap in a fake classifier.
        builder.UseSetting("Ai:Provider", "Disabled");
        // Test classes reuse one user; the limit itself is covered by a dedicated test.
        builder.UseSetting("RateLimiting:PermitsPerMinute", "10000");

        builder.ConfigureTestServices(services =>
        {
            // Test-only endpoints that exercise the pipeline; they never ship in the API assembly.
            services.AddControllers().AddApplicationPart(typeof(ApiFactory).Assembly);
            services.AddSingleton<IValidator<EchoRequest>, EchoRequestValidator>();

            // Swap only the key source: static signing key instead of Entra's metadata endpoint.
            // Audience, lifetime, signature and scope validation stay exactly as in production.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = TestTokens.Issuer };
                configuration.SigningKeys.Add(TestTokens.SigningKey);
                options.Configuration = configuration;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                options.TokenValidationParameters.IssuerValidator = null;
                options.TokenValidationParameters.ValidIssuers = [TestTokens.Issuer, TestTokens.IssuerV1];
            });
        });
    }

    public HttpClient CreateAuthenticatedClient(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public HttpClient CreateAuthenticatedClient() => CreateAuthenticatedClient(TestTokens.Create());
}
