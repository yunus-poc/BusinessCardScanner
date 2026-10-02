using System.Diagnostics;

namespace BusinessCard.Api.Errors;

internal static class ProblemDetailsSetup
{
    public const string TraceIdKey = "traceId";
    public const string CodeKey = "code";
    public const string RetryableKey = "retryable";

    /// <summary>
    /// Every ProblemDetails, whether written by our exception handler, by MVC model validation, or by
    /// status-code pages (404/401/403), passes through this one customization, so the contract is uniform.
    /// </summary>
    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services)
    {
        return services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            var status = problem.Status ?? context.HttpContext.Response.StatusCode;

            // The W3C trace id is what Serilog records, so support can join a client report to server logs.
            problem.Extensions[TraceIdKey] = CurrentTraceId(context.HttpContext);
            problem.Extensions.TryAdd(CodeKey, ErrorResponseMapping.CodeFor(status));
            problem.Extensions.TryAdd(RetryableKey, ErrorResponseMapping.IsRetryable(status));
        });
    }

    public static string CurrentTraceId(HttpContext httpContext) =>
        Activity.Current?.TraceId.ToHexString() ?? httpContext.TraceIdentifier;
}
