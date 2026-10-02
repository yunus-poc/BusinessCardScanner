using BusinessCard.Application.Observability;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace BusinessCard.Api.Observability;

/// <summary>Configuration section "Telemetry".</summary>
public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    /// <summary>OTLP collector (e.g. http://otel-collector:4317). Unset: no export.</summary>
    public Uri? OtlpEndpoint { get; set; }

    public string ServiceName { get; set; } = "businesscard-api";
}

internal static class TelemetrySetup
{
    /// <summary>
    /// Exports metrics only, and only when an OTLP endpoint is configured. Traces are
    /// deliberately not exported: HTTP client spans carry request URLs, and the Graph
    /// duplicate search has an email address in its query string. Without an endpoint the
    /// meters can still be read live with <c>dotnet-counters monitor --counters BusinessCard</c>.
    /// </summary>
    public static IServiceCollection AddMetricsExport(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>() ?? new TelemetryOptions();
        if (options.OtlpEndpoint is null)
        {
            return services;
        }

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(options.ServiceName))
            .WithMetrics(metrics => metrics
                .AddMeter(BusinessCardMetrics.MeterName)
                // Built-in ASP.NET Core meters: request duration tagged with the route template, not the URL.
                .AddMeter("Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Server.Kestrel", "Microsoft.AspNetCore.RateLimiting")
                .AddOtlpExporter(otlp => otlp.Endpoint = options.OtlpEndpoint));
        return services;
    }
}
