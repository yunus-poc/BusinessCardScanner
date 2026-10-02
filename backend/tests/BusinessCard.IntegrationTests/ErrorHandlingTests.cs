using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BusinessCard.Application.Errors;
using BusinessCard.IntegrationTests.Infrastructure;

namespace BusinessCard.IntegrationTests;

public sealed class ErrorHandlingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task UnknownRoute_ReturnsProblemDetailsWithCodeAndMatchingTraceId()
    {
        var response = await factory.CreateAuthenticatedClient().GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal(ErrorCodes.NotFound, problem.GetProperty("code").GetString());
        Assert.False(problem.GetProperty("retryable").GetBoolean());

        var traceId = problem.GetProperty("traceId").GetString();
        Assert.Matches("^[0-9a-f]{32}$", traceId);
        Assert.Equal(traceId, response.Headers.GetValues("X-Trace-Id").Single());
    }

    [Theory]
    [InlineData(ErrorCodes.GraphRateLimited, HttpStatusCode.TooManyRequests, true)]
    [InlineData(ErrorCodes.GraphUnavailable, HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(ErrorCodes.RateLimited, HttpStatusCode.TooManyRequests, true)]
    [InlineData(ErrorCodes.GraphContactCreateFailed, HttpStatusCode.BadGateway, true)]
    [InlineData(ErrorCodes.GraphAuthorizationFailed, HttpStatusCode.Forbidden, false)]
    [InlineData(ErrorCodes.ContactNotFound, HttpStatusCode.NotFound, false)]
    public async Task AppException_MapsToStatusCodeAndRetryability(string code, HttpStatusCode expectedStatus, bool retryable)
    {
        var response = await _client.GetAsync($"/test/pipeline/app-error/{code}");

        Assert.Equal(expectedStatus, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal(retryable, problem.GetProperty("retryable").GetBoolean());
        Assert.Equal("Safe user-facing message.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task RateLimited_ForwardsRetryAfterRoundedUpToWholeSeconds()
    {
        var response = await _client.GetAsync($"/test/pipeline/app-error/{ErrorCodes.GraphRateLimited}");

        Assert.Equal(TimeSpan.FromSeconds(7), response.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task UnexpectedException_Returns500WithoutLeakingInternals()
    {
        var response = await _client.GetAsync("/test/pipeline/unexpected");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(PipelineTestController.UnexpectedSecret, body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", body, StringComparison.Ordinal);

        var problem = JsonDocument.Parse(body).RootElement;
        Assert.Equal(ErrorCodes.InternalError, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task InvalidBody_ReturnsValidationProblemKeyedByJsonPath()
    {
        var response = await _client.PostAsJsonAsync("/test/pipeline/echo", new
        {
            displayName = "",
            items = new[] { new { text = "ok" }, new { text = "far too long" } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal(ErrorCodes.ValidationFailed, problem.GetProperty("code").GetString());

        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("displayName", out _));
        Assert.True(errors.TryGetProperty("items[1].text", out _));
        Assert.False(errors.TryGetProperty("items[0].text", out _));
    }

    [Fact]
    public async Task MalformedJson_ReturnsValidationFailed()
    {
        using var content = new StringContent("{ \"displayName\": ", Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/test/pipeline/echo", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal(ErrorCodes.ValidationFailed, problem.GetProperty("code").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task ValidBody_ReachesTheAction()
    {
        var response = await _client.PostAsJsonAsync("/test/pipeline/echo", new { displayName = "Card" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }
}
