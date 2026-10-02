using System.Globalization;
using BusinessCard.Application.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace BusinessCard.Api.Errors;

/// <summary>
/// Converts every exception that escapes a request into ProblemDetails. Internal details
/// (messages of unexpected exceptions, stack traces, upstream bodies) never reach the client.
/// </summary>
internal sealed partial class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away; nobody is listening for a response body.
            LogClientAborted(logger);
            httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            return true;
        }

        var problem = exception switch
        {
            AppException appException => FromAppException(httpContext, appException),
            BadHttpRequestException badRequest => FromBadRequest(badRequest),
            _ => FromUnexpected(exception),
        };

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
        });
    }

    private ProblemDetails FromAppException(HttpContext httpContext, AppException exception)
    {
        var status = ErrorResponseMapping.StatusFor(exception.Code);

        // Expected failures: one line with the code, no stack trace, no message (it may echo input).
        LogAppException(logger, exception.Code, status);

        if (exception.RetryAfter is { } retryAfter)
        {
            var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
            httpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }

        return new ProblemDetails
        {
            Status = status,
            Title = ReasonPhrases.GetReasonPhrase(status),
            Detail = exception.Message,
            Extensions = { [ProblemDetailsSetup.CodeKey] = exception.Code },
        };
    }

    private ProblemDetails FromBadRequest(BadHttpRequestException exception)
    {
        // Raised by Kestrel/hosting for oversized or malformed bodies; the status code is authoritative.
        LogBadRequest(logger, exception.StatusCode);
        return new ProblemDetails
        {
            Status = exception.StatusCode,
            Title = ReasonPhrases.GetReasonPhrase(exception.StatusCode),
        };
    }

    private ProblemDetails FromUnexpected(Exception exception)
    {
        LogUnexpected(logger, exception);
        return new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Detail = "Please try again. If the problem persists, contact support with the trace id.",
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Request aborted by client")]
    private static partial void LogClientAborted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Request failed with {ErrorCode} ({StatusCode})")]
    private static partial void LogAppException(ILogger logger, string errorCode, int statusCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rejected bad request ({StatusCode})")]
    private static partial void LogBadRequest(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    private static partial void LogUnexpected(ILogger logger, Exception exception);
}
