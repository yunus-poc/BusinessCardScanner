using System.Net;
using System.Text.Json;
using BusinessCard.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace BusinessCard.IntegrationTests;

public sealed class HealthAndDocsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    public async Task HealthEndpoints_ReturnHealthyJson(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Swagger_IsNotExposedOutsideDevelopment()
    {
        var response = await factory.CreateClient().GetAsync("/swagger/v1/swagger.json");

        // Not mapped, so the deny-by-default policy answers before routing can say 404.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_DocumentGeneratesInDevelopment()
    {
        using var devFactory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));

        var response = await devFactory.CreateClient().GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
