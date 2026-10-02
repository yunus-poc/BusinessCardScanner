using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BusinessCard.Application.Contacts;
using BusinessCard.Application.Errors;
using BusinessCard.Application.Observability;
using BusinessCard.Domain.Contacts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using PhoneNumbers;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace BusinessCard.Infrastructure.Graph;

/// <summary>
/// Outlook contacts through Microsoft Graph REST v1.0, called On-Behalf-Of the signed-in
/// user with the delegated Contacts.ReadWrite scope.
/// </summary>
/// <remarks>
/// <para>Retry rules exist because <c>POST /me/contacts</c> is not idempotent: retrying a request
/// that timed out after Graph executed it would create a duplicate contact. So:</para>
/// <list type="bullet">
/// <item>429, or 503 with Retry-After, means Graph rejected the request without running it, so one
/// inline retry is safe for every method, if the wait is short (≤ MaxInlineRetryDelaySeconds).</item>
/// <item>Timeouts, connection failures and 503/504 without Retry-After are retried once only for
/// idempotent calls (GET, PATCH).</item>
/// <item>Longer back-offs are returned immediately as GRAPH_RATE_LIMITED with Retry-After, so the
/// app can tell the user instead of holding the request open.</item>
/// </list>
/// Logs carry operation, status, timing, attempt and Graph's request id, never contact data.
/// </remarks>
internal sealed partial class GraphContactService(
    HttpClient httpClient,
    IAuthorizationHeaderProvider authorization,
    IOptions<GraphOptions> options,
    BusinessCardMetrics metrics,
    ILogger<GraphContactService> logger) : IMicrosoftGraphContactService
{
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(500);
    private readonly GraphOptions _options = options.Value;

    public async Task<SavedContact> CreateAsync(ContactDetails contact, CancellationToken cancellationToken)
    {
        var body = GraphContactMapper.ToCreateBody(contact);
        var created = await SendAsync(HttpMethod.Post, "me/contacts", body, idempotent: false, Operation.Create, cancellationToken);
        return GraphContactMapper.ReadSaved(created, contact.DisplayName);
    }

    public async Task<SavedContact> UpdateAsync(string contactId, ContactDetails contact, CancellationToken cancellationToken)
    {
        if (!IsValidContactId(contactId))
        {
            throw new AppException(ErrorCodes.ContactNotFound, "The contact to update was not found.");
        }
        var path = $"me/contacts/{Uri.EscapeDataString(contactId)}";

        var existing = await SendAsync(HttpMethod.Get, path + "?$select=emailAddresses,businessPhones", null, idempotent: true, Operation.Read, cancellationToken);
        var (emails, phones) = GraphContactMapper.ReadMergeFields(existing);

        var body = GraphContactMapper.ToUpdateBody(contact, emails, phones);
        var updated = await SendAsync(HttpMethod.Patch, path, body, idempotent: true, Operation.Update, cancellationToken);
        return GraphContactMapper.ReadSaved(updated, contact.DisplayName);
    }

    /// <remarks>
    /// Up to three filtered queries run in parallel (one round-trip of latency): any shared email,
    /// the mobile number in the formats a contact is typically stored in (Graph compares strings
    /// exactly), and first + last name. A query Graph rejects is skipped rather than failing the
    /// whole check; throttling or an outage still fails it.
    /// </remarks>
    public async Task<IReadOnlyList<ExistingContact>> FindCandidatesAsync(ContactDetails contact, CancellationToken cancellationToken)
    {
        var queries = new List<(string Name, string Filter)>();
        if (contact.Emails.Count > 0)
        {
            var anyEmail = string.Join(" or ", contact.Emails.Select(e => $"a/address eq {Quote(e)}"));
            queries.Add(("email", $"emailAddresses/any(a:{anyEmail})"));
        }
        if (MobileVariants(contact.MobilePhone) is { Count: > 0 } variants)
        {
            queries.Add(("mobile", string.Join(" or ", variants.Select(v => $"mobilePhone eq {Quote(v)}"))));
        }
        if (contact.FirstName is not null && contact.LastName is not null)
        {
            queries.Add(("name", $"givenName eq {Quote(contact.FirstName)} and surname eq {Quote(contact.LastName)}"));
        }

        var results = await Task.WhenAll(queries.Select(q => QueryAsync(q.Name, q.Filter, cancellationToken)));
        return results.SelectMany(r => r).ToList();
    }

    private async Task<IReadOnlyList<ExistingContact>> QueryAsync(string name, string filter, CancellationToken cancellationToken)
    {
        var path = $"me/contacts?$filter={Uri.EscapeDataString(filter)}&$select={CandidateFields}&$top=10";
        try
        {
            var page = await SendAsync(HttpMethod.Get, path, null, idempotent: true, Operation.Search, cancellationToken);
            return GraphContactMapper.ReadCandidates(page);
        }
        catch (UnsupportedFilterException)
        {
            LogQuerySkipped(logger, name);
            return [];
        }
    }

    private const string CandidateFields = "id,displayName,givenName,surname,companyName,emailAddresses,mobilePhone,businessPhones";

    /// <summary>OData string literal: single quotes are doubled, so "O'Brien" cannot break the filter.</summary>
    internal static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    /// <summary>"+90 532 123 45 67", "+905321234567" and "0532 123 45 67" for the same number.</summary>
    internal static IReadOnlyList<string> MobileVariants(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return [];
        }
        var util = PhoneNumberUtil.GetInstance();
        try
        {
            var number = util.Parse(phone, PhoneDefaults.Region);
            if (!util.IsValidNumber(number))
            {
                return [phone];
            }
            return new[] { PhoneNumberFormat.INTERNATIONAL, PhoneNumberFormat.E164, PhoneNumberFormat.NATIONAL }
                .Select(format => util.Format(number, format))
                .Append(phone)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        catch (NumberParseException)
        {
            return [phone];
        }
    }

    /// <summary>Graph ids are URL-safe base64-like strings; anything else is rejected before calling Graph.</summary>
    internal static bool IsValidContactId(string id) =>
        id.Length is > 0 and <= ContactLimits.MaxContactIdLength && ContactId().IsMatch(id);

    private async Task<JsonNode?> SendAsync(
        HttpMethod method,
        string path,
        JsonObject? body,
        bool idempotent,
        Operation operation,
        CancellationToken cancellationToken)
    {
        var authorizationHeader = await AuthorizationHeaderAsync(cancellationToken);
        var stopwatch = Stopwatch.StartNew();

        for (var attempt = 1; ; attempt++)
        {
            var requestId = Guid.NewGuid().ToString();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorizationHeader);
            // Correlates our logs with Microsoft's if a support case is ever needed.
            request.Headers.Add("client-request-id", requestId);
            if (body is not null)
            {
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(request, timeout.Token);
            }
            catch (Exception e) when (e is HttpRequestException || (e is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // The request may have reached Graph: only idempotent calls are repeated.
                if (idempotent && attempt == 1)
                {
                    continue;
                }
                LogFailure(logger, operation, 0, attempt, stopwatch.ElapsedMilliseconds, requestId, e is HttpRequestException ? "connection" : "timeout");
                metrics.GraphCompleted(OperationName(operation), ErrorCodes.GraphUnavailable, stopwatch.Elapsed);
                throw new AppException(ErrorCodes.GraphUnavailable, "Outlook did not respond. Please try again.", e);
            }

            using (response)
            {
                var status = (int)response.StatusCode;
                if (response.IsSuccessStatusCode)
                {
                    LogSuccess(logger, operation, status, attempt, stopwatch.ElapsedMilliseconds, requestId);
                    metrics.GraphCompleted(OperationName(operation), "success", stopwatch.Elapsed);
                    var text = await response.Content.ReadAsStringAsync(cancellationToken);
                    return text.Length == 0 ? null : JsonNode.Parse(text);
                }

                var retryAfter = RetryAfter(response);
                if (IsThrottled(response.StatusCode))
                {
                    var notExecuted = status == 429 || retryAfter is not null;
                    var shortWait = (retryAfter ?? DefaultRetryDelay) <= TimeSpan.FromSeconds(_options.MaxInlineRetryDelaySeconds);
                    if (attempt == 1 && shortWait && (notExecuted || idempotent))
                    {
                        await Task.Delay(retryAfter ?? DefaultRetryDelay, cancellationToken);
                        continue;
                    }
                }

                var graphCode = await GraphErrorCodeAsync(response, cancellationToken);
                LogFailure(logger, operation, status, attempt, stopwatch.ElapsedMilliseconds, requestId, graphCode ?? "none");
                if (operation == Operation.Search && response.StatusCode == HttpStatusCode.BadRequest)
                {
                    metrics.GraphCompleted(OperationName(operation), "filter_rejected", stopwatch.Elapsed);
                    throw new UnsupportedFilterException();
                }
                var failure = MapFailure(response.StatusCode, operation, graphCode, retryAfter);
                metrics.GraphCompleted(OperationName(operation), failure.Code, stopwatch.Elapsed);
                throw failure;
            }
        }
    }

    private async Task<string> AuthorizationHeaderAsync(CancellationToken cancellationToken)
    {
        // MSAL's own HTTP timeout is 100 s; a slow token endpoint must not hold the save that long.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        try
        {
            // On-Behalf-Of: exchanges the caller's API token for a delegated Graph token (cached).
            return await authorization.CreateAuthorizationHeaderForUserAsync(_options.Scopes, cancellationToken: timeout.Token);
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            LogTokenFailure(logger, "timeout");
            throw new AppException(ErrorCodes.GraphUnavailable, "Signing in to Outlook took too long. Please try again.", e);
        }
        catch (Exception e) when (e is MicrosoftIdentityWebChallengeUserException or MsalUiRequiredException)
        {
            // Consent missing, password changed, or a Conditional Access requirement.
            LogTokenFailure(logger, "interaction_required");
            throw new AppException(ErrorCodes.GraphAuthorizationFailed,
                "Outlook access needs to be approved again. Sign out, sign in, and retry; if it persists, ask IT to grant consent.", e);
        }
        catch (MsalException e)
        {
            // Typically server misconfiguration (missing or expired API client secret/certificate).
            LogTokenFailure(logger, e.ErrorCode);
            throw new AppException(ErrorCodes.GraphAuthorizationFailed,
                "Outlook access is not configured correctly. Please contact IT support.", e);
        }
    }

    private static AppException MapFailure(HttpStatusCode status, Operation operation, string? graphCode, TimeSpan? retryAfter) =>
        (status, operation, graphCode) switch
        {
            (HttpStatusCode.TooManyRequests, _, _) => new AppException(
                ErrorCodes.GraphRateLimited, "Outlook is busy. Please try again shortly.") { RetryAfter = retryAfter },
            (_, _, "MailboxNotEnabledForRESTAPI" or "MailboxNotSupportedForRESTAPI") => new AppException(
                ErrorCodes.GraphAuthorizationFailed, "Your account has no Exchange Online mailbox, so contacts cannot be saved."),
            (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, _, _) => new AppException(
                ErrorCodes.GraphAuthorizationFailed, "Your account is not allowed to save Outlook contacts."),
            (HttpStatusCode.NotFound, Operation.Read or Operation.Update, _) => new AppException(
                ErrorCodes.ContactNotFound, "The contact to update no longer exists in Outlook."),
            (>= HttpStatusCode.InternalServerError, _, _) => new AppException(
                ErrorCodes.GraphUnavailable, "Outlook is temporarily unavailable. Please try again.") { RetryAfter = retryAfter },
            _ => new AppException(ErrorCodes.GraphContactCreateFailed, "Outlook could not save this contact."),
        };

    private static bool IsThrottled(HttpStatusCode status) =>
        status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta;
        }
        if (header?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }
        return null;
    }

    /// <summary>Graph's error.code only (e.g. "ErrorInvalidRequest"); messages are never logged.</summary>
    private static async Task<string?> GraphErrorCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonNode.Parse(text)?["error"]?["code"]?.GetValue<string>();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string OperationName(Operation operation) => operation switch
    {
        Operation.Create => "create",
        Operation.Read => "read",
        Operation.Update => "update",
        _ => "search",
    };

    internal enum Operation
    {
        Create,
        Read,
        Update,
        Search,
    }

    /// <summary>Graph rejected a duplicate-search filter (400); that one query is skipped.</summary>
    private sealed class UnsupportedFilterException : Exception;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Graph rejected the {DuplicateQuery} duplicate query; continuing without it")]
    private static partial void LogQuerySkipped(ILogger logger, string duplicateQuery);

    [GeneratedRegex(@"^[A-Za-z0-9\-_=]+$")]
    private static partial Regex ContactId();

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Graph {GraphOperation} succeeded: HTTP {StatusCode} after {GraphAttempts} attempt(s) in {GraphDurationMs} ms (client-request-id {GraphRequestId})")]
    private static partial void LogSuccess(ILogger logger, Operation graphOperation, int statusCode, int graphAttempts, long graphDurationMs, string graphRequestId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Graph {GraphOperation} failed: HTTP {StatusCode} ({GraphErrorCode}) after {GraphAttempts} attempt(s) in {GraphDurationMs} ms (client-request-id {GraphRequestId})")]
    private static partial void LogFailure(ILogger logger, Operation graphOperation, int statusCode, int graphAttempts, long graphDurationMs, string graphRequestId, string graphErrorCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Graph token acquisition (On-Behalf-Of) failed: {MsalErrorCode}")]
    private static partial void LogTokenFailure(ILogger logger, string msalErrorCode);
}
