using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BusinessCard.Api.Health;

internal static class HealthEndpoints
{
    /// <summary>Tag for checks that belong to readiness (dependencies), not liveness.</summary>
    public const string ReadyTag = Infrastructure.DependencyInjection.ReadinessTag;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapApiHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        // Liveness: the process is up and serving requests. Runs no dependency checks, so a slow
        // LLM or Graph outage never makes the orchestrator restart a healthy API.
        endpoints.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteResponse,
        }).AllowAnonymous();

        // Readiness: dependency status. The LLM check reports Degraded (still HTTP 200) when the
        // model is down, because the parse flow falls back to deterministic extraction.
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WriteResponse,
        }).AllowAnonymous();

        return endpoints;
    }

    // Deliberately omits descriptions and exception text: health endpoints are anonymous.
    private static Task WriteResponse(HttpContext httpContext, HealthReport report)
    {
        var body = new
        {
            status = report.Status.ToString(),
            durationMs = (int)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = (int)entry.Value.Duration.TotalMilliseconds,
            }),
        };

        httpContext.Response.ContentType = "application/json";
        return httpContext.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions));
    }
}
