using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace BusinessCard.Infrastructure.Llm;

/// <summary>
/// Readiness signal for the inference service. Reports Degraded, never Unhealthy:
/// scanning keeps working without the model (deterministic fallback), so an LLM
/// outage must not take the API out of the load balancer.
/// </summary>
internal sealed class LlmHealthCheck(IHttpClientFactory httpClientFactory, IOptions<AiOptions> options) : IHealthCheck
{
    public const string HttpClientName = "llm-health";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (options.Value.Provider == AiProvider.Disabled)
        {
            return HealthCheckResult.Healthy("LLM disabled by configuration.");
        }

        try
        {
            // llama-server: 503 while loading, 200 when ready; /health needs no API key.
            using var response = await httpClientFactory.CreateClient(HttpClientName).GetAsync("health", cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Degraded($"LLM returned HTTP {(int)response.StatusCode}.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return HealthCheckResult.Degraded("LLM unreachable.");
        }
    }
}
