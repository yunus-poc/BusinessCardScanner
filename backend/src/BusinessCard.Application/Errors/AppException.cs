namespace BusinessCard.Application.Errors;

/// <summary>
/// An expected, classified failure that the API translates into a ProblemDetails response.
/// </summary>
/// <remarks>
/// <see cref="Exception.Message"/> is returned to the client as <c>detail</c>, so it must be a
/// safe, user-facing sentence with no PII, tokens or upstream response bodies.
/// </remarks>
public sealed class AppException : Exception
{
    public AppException(string code, string safeMessage, Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        Code = code;
    }

    public string Code { get; }

    /// <summary>Upstream back-off hint (for example Graph's Retry-After), forwarded to the client.</summary>
    public TimeSpan? RetryAfter { get; init; }
}
