namespace BusinessCard.Application.Errors;

/// <summary>
/// Stable, machine-readable error codes returned in the ProblemDetails <c>code</c> extension.
/// The mobile app switches on these strings, so treat them as part of the public API contract.
/// </summary>
public static class ErrorCodes
{
    // There are deliberately no OCR or LLM error codes: OCR runs on the device, and an LLM
    // problem never fails a parse; it is reported as data in meta.llmStatus instead.
    public const string ValidationFailed = "VALIDATION_FAILED";

    public const string GraphAuthorizationFailed = "GRAPH_AUTHORIZATION_FAILED";
    public const string GraphContactCreateFailed = "GRAPH_CONTACT_CREATE_FAILED";
    public const string GraphRateLimited = "GRAPH_RATE_LIMITED";
    public const string GraphUnavailable = "GRAPH_UNAVAILABLE";
    public const string ContactNotFound = "CONTACT_NOT_FOUND";

    // Generic codes for responses produced by the framework rather than by application code.
    public const string Unauthenticated = "UNAUTHENTICATED";
    /// <summary>This API's own per-user limit (not Graph's: that is GRAPH_RATE_LIMITED).</summary>
    public const string RateLimited = "RATE_LIMITED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
    public const string UnsupportedMediaType = "UNSUPPORTED_MEDIA_TYPE";
    public const string InternalError = "INTERNAL_ERROR";
}
