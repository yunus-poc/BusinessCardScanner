using BusinessCard.Application.Errors;

namespace BusinessCard.Api.Errors;

/// <summary>
/// Single source of truth for how error codes map to HTTP status and whether the client may retry.
/// </summary>
internal static class ErrorResponseMapping
{
    public static int StatusFor(string code) => code switch
    {
        ErrorCodes.ValidationFailed => StatusCodes.Status400BadRequest,
        ErrorCodes.ContactNotFound => StatusCodes.Status404NotFound,
        ErrorCodes.GraphAuthorizationFailed => StatusCodes.Status403Forbidden,
        ErrorCodes.GraphRateLimited => StatusCodes.Status429TooManyRequests,
        ErrorCodes.RateLimited => StatusCodes.Status429TooManyRequests,
        ErrorCodes.GraphContactCreateFailed => StatusCodes.Status502BadGateway,
        ErrorCodes.GraphUnavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };

    /// <summary>Default code for responses the framework produces (404 routing, 401 auth, 413 limits…).</summary>
    public static string CodeFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => ErrorCodes.ValidationFailed,
        StatusCodes.Status401Unauthorized => ErrorCodes.Unauthenticated,
        StatusCodes.Status403Forbidden => ErrorCodes.Forbidden,
        StatusCodes.Status404NotFound => ErrorCodes.NotFound,
        StatusCodes.Status413PayloadTooLarge => ErrorCodes.PayloadTooLarge,
        StatusCodes.Status415UnsupportedMediaType => ErrorCodes.UnsupportedMediaType,
        StatusCodes.Status429TooManyRequests => ErrorCodes.RateLimited,
        _ => ErrorCodes.InternalError,
    };

    /// <summary>
    /// True when repeating the same request later can succeed without the user changing anything.
    /// The mobile app shows a Retry action only for these.
    /// </summary>
    public static bool IsRetryable(int status) => status is
        StatusCodes.Status408RequestTimeout or
        StatusCodes.Status429TooManyRequests or
        StatusCodes.Status502BadGateway or
        StatusCodes.Status503ServiceUnavailable or
        StatusCodes.Status504GatewayTimeout;
}
