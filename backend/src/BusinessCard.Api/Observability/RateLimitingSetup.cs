using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using BusinessCard.Api.Errors;
using BusinessCard.Application.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace BusinessCard.Api.Observability;

/// <summary>Configuration section "RateLimiting".</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Per signed-in user. A person scanning cards makes ~3 calls per card.</summary>
    public int PermitsPerMinute { get; set; } = 60;
}

internal static class RateLimitingSetup
{
    private const string ObjectIdClaim = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    /// <summary>
    /// Per-user fixed window, keyed by the token's object id. Protects the shared LLM and the
    /// Graph quota from one misbehaving client. Health probes are never limited.
    /// </summary>
    public static IServiceCollection AddPerUserRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new RateLimitingOptions();

        return services.AddRateLimiter(limiter =>
        {
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (context.Request.Path.StartsWithSegments("/health"))
                {
                    return RateLimitPartition.GetNoLimiter("health");
                }
                return RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.PermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });

            limiter.OnRejected = async (rejected, cancellationToken) =>
            {
                var http = rejected.HttpContext;
                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }
                await http.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = http,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = ReasonPhrases.GetReasonPhrase(StatusCodes.Status429TooManyRequests),
                        Detail = "Too many requests. Please wait a moment and try again.",
                        Extensions = { [ProblemDetailsSetup.CodeKey] = ErrorCodes.RateLimited },
                    },
                });
            };
        });
    }

    /// <summary>Authenticated users by object id; anything else (rejected later anyway) by client IP.</summary>
    private static string PartitionKey(HttpContext context) =>
        context.User.FindFirstValue(ObjectIdClaim)
        ?? context.User.FindFirstValue("oid")
        ?? "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
}
