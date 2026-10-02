using BusinessCard.Api.Errors;
using Serilog;
using Serilog.Events;

namespace BusinessCard.Api.Observability;

internal static class RequestLoggingSetup
{
    public const string TraceIdHeader = "X-Trace-Id";

    /// <summary>
    /// One structured line per request. Only an allow-list of properties is recorded: the route
    /// template (never the query string or body), status, duration and trace id. No PII.
    /// </summary>
    public static IApplicationBuilder UseApiRequestLogging(this IApplicationBuilder app)
    {
        return app.UseSerilogRequestLogging(options =>
        {
            // {Elapsed} is the request duration in milliseconds (the "DurationMs" metric).
            options.MessageTemplate = "HTTP {RequestMethod} {Endpoint} responded {StatusCode} in {Elapsed:0} ms";
            options.EnrichDiagnosticContext = (diagnostics, httpContext) =>
            {
                diagnostics.Set("Endpoint", EndpointName(httpContext));
                diagnostics.Set("TraceId", ProblemDetailsSetup.CurrentTraceId(httpContext));
            };
            options.GetLevel = (httpContext, _, exception) => httpContext switch
            {
                _ when exception is not null || httpContext.Response.StatusCode >= 500 => LogEventLevel.Error,
                // Probes hit /health every few seconds; keep them out of normal log volume.
                _ when httpContext.Request.Path.StartsWithSegments("/health") => LogEventLevel.Verbose,
                _ => LogEventLevel.Information,
            };
        });
    }

    /// <summary>Returns the trace id on every response so the app can show it on error screens.</summary>
    public static IApplicationBuilder UseTraceIdResponseHeader(this IApplicationBuilder app)
    {
        return app.Use(async (httpContext, next) =>
        {
            httpContext.Response.OnStarting(() =>
            {
                httpContext.Response.Headers[TraceIdHeader] = ProblemDetailsSetup.CurrentTraceId(httpContext);
                return Task.CompletedTask;
            });
            await next(httpContext);
        });
    }

    private static string EndpointName(HttpContext httpContext) =>
        (httpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
}
