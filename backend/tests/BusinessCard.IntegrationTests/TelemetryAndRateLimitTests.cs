using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessCard.Application.Errors;
using BusinessCard.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace BusinessCard.IntegrationTests;

public sealed class TelemetryAndRateLimitTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Client_scan_timings_are_accepted()
    {
        var response = await factory.CreateAuthenticatedClient()
            .PostAsJsonAsync("/api/telemetry/scan", new { ocrMs = 410, ocrToPreviewMs = 1_320, llmStatus = "ok" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData(-1, 100, "ok")]
    [InlineData(100, 999_999, "ok")]
    [InlineData(100, 100, "Ahmet Yılmaz")]
    public async Task Implausible_or_free_text_telemetry_is_rejected(int ocrMs, int previewMs, string status)
    {
        var response = await factory.CreateAuthenticatedClient()
            .PostAsJsonAsync("/api/telemetry/scan", new { ocrMs, ocrToPreviewMs = previewMs, llmStatus = status });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Per_user_limit_returns_429_with_retry_after()
    {
        using var limited = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:PermitsPerMinute", "2"));
        var client = limited.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.Create());
        var body = new { ocrMs = 1, ocrToPreviewMs = 1, llmStatus = "ok" };

        await client.PostAsJsonAsync("/api/telemetry/scan", body);
        await client.PostAsJsonAsync("/api/telemetry/scan", body);
        var third = await client.PostAsJsonAsync("/api/telemetry/scan", body);

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.NotNull(third.Headers.RetryAfter?.Delta);
        var problem = JsonDocument.Parse(await third.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(ErrorCodes.RateLimited, problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("retryable").GetBoolean());

        // Health probes are never limited.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }
}
