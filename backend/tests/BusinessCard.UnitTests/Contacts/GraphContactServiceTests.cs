using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using BusinessCard.Application.Errors;
using BusinessCard.Domain.Contacts;
using BusinessCard.Infrastructure.Graph;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Client;

namespace BusinessCard.UnitTests.Contacts;

public sealed class GraphContactServiceTests
{
    private static readonly ContactDetails Contact = new()
    {
        FirstName = "Ahmet",
        LastName = "Yılmaz",
        DisplayName = "Ahmet Yılmaz",
        CompanyName = "ACME Technology",
        JobTitle = "Enterprise Solutions Manager",
        Emails = ["ahmet.yilmaz@acme.com.tr"],
        MobilePhone = "+90 532 123 45 67",
        BusinessPhones = ["+90 212 555 00 00"],
        Website = "https://www.acme.com.tr",
        Street = "Maslak Mah. Büyükdere Cad. No:255, Sarıyer",
        City = "İstanbul",
        PostalCode = "34485",
        Country = "Türkiye",
    };

    private const string CreatedJson = """{"id":"AAMkAD-123_x=","displayName":"Ahmet Yılmaz"}""";

    [Fact]
    public async Task Create_posts_the_mapped_contact_with_an_obo_token()
    {
        var graph = new ScriptedGraph(Respond(HttpStatusCode.Created, CreatedJson));

        var saved = await Service(graph).CreateAsync(Contact, CancellationToken.None);

        Assert.Equal(new SavedContact("AAMkAD-123_x=", "Ahmet Yılmaz"), saved);
        var request = graph.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://graph.test/v1.0/me/contacts", request.Uri);
        Assert.Equal("Bearer obo-token", request.Authorization);
        Assert.NotNull(request.ClientRequestId);
        var body = JsonNode.Parse(request.Body)!;
        Assert.Equal("Ahmet", body["givenName"]!.GetValue<string>());
        Assert.Equal("ahmet.yilmaz@acme.com.tr", body["emailAddresses"]![0]!["address"]!.GetValue<string>());
        Assert.Equal("İstanbul", body["businessAddress"]!["city"]!.GetValue<string>());
    }

    [Fact]
    public async Task Short_throttling_is_retried_once_honouring_retry_after()
    {
        var graph = new ScriptedGraph(Throttled(HttpStatusCode.TooManyRequests, seconds: 1), Respond(HttpStatusCode.Created, CreatedJson));

        var saved = await Service(graph).CreateAsync(Contact, CancellationToken.None);

        Assert.Equal("AAMkAD-123_x=", saved.Id);
        Assert.Equal(2, graph.Requests.Count);
    }

    [Fact]
    public async Task Long_throttling_is_returned_to_the_caller_with_retry_after()
    {
        var graph = new ScriptedGraph(Throttled(HttpStatusCode.TooManyRequests, seconds: 30));

        var error = await Assert.ThrowsAsync<AppException>(() => Service(graph).CreateAsync(Contact, CancellationToken.None));

        Assert.Equal(ErrorCodes.GraphRateLimited, error.Code);
        Assert.Equal(TimeSpan.FromSeconds(30), error.RetryAfter);
        Assert.Single(graph.Requests);
    }

    [Fact]
    public async Task Create_is_never_retried_when_graph_might_have_executed_it()
    {
        // 503 without Retry-After: Graph may have created the contact before failing.
        var graph = new ScriptedGraph(Respond(HttpStatusCode.ServiceUnavailable, "{}"), Respond(HttpStatusCode.Created, CreatedJson));

        var error = await Assert.ThrowsAsync<AppException>(() => Service(graph).CreateAsync(Contact, CancellationToken.None));

        Assert.Equal(ErrorCodes.GraphUnavailable, error.Code);
        Assert.Single(graph.Requests);
    }

    [Fact]
    public async Task Create_is_not_retried_after_a_timeout()
    {
        var graph = new ScriptedGraph(Hang(), Respond(HttpStatusCode.Created, CreatedJson));

        var error = await Assert.ThrowsAsync<AppException>(() => Service(graph, timeoutSeconds: 1).CreateAsync(Contact, CancellationToken.None));

        Assert.Equal(ErrorCodes.GraphUnavailable, error.Code);
        Assert.Single(graph.Requests);
    }

    [Fact]
    public async Task Service_unavailable_with_retry_after_is_safe_to_retry_once()
    {
        var graph = new ScriptedGraph(Throttled(HttpStatusCode.ServiceUnavailable, seconds: 1), Respond(HttpStatusCode.Created, CreatedJson));

        await Service(graph).CreateAsync(Contact, CancellationToken.None);

        Assert.Equal(2, graph.Requests.Count);
    }

    [Fact]
    public async Task Update_merges_existing_emails_and_phones_then_patches()
    {
        const string existing = """{"emailAddresses":[{"address":"ahmet@old-company.com"},{"address":"AHMET.YILMAZ@acme.com.tr"}],"businessPhones":["+90 216 000 00 00"]}""";
        var graph = new ScriptedGraph(Respond(HttpStatusCode.OK, existing), Respond(HttpStatusCode.OK, CreatedJson));

        await Service(graph).UpdateAsync("AAMkAD-123_x=", Contact, CancellationToken.None);

        Assert.Equal(HttpMethod.Get, graph.Requests[0].Method);
        Assert.Equal("https://graph.test/v1.0/me/contacts/AAMkAD-123_x%3D?$select=emailAddresses,businessPhones", graph.Requests[0].Uri);
        Assert.Equal(HttpMethod.Patch, graph.Requests[1].Method);
        var body = JsonNode.Parse(graph.Requests[1].Body)!;
        Assert.Equal(
            ["ahmet.yilmaz@acme.com.tr", "ahmet@old-company.com"],
            body["emailAddresses"]!.AsArray().Select(e => e!["address"]!.GetValue<string>()));
        Assert.Equal(
            ["+90 212 555 00 00", "+90 216 000 00 00"],
            body["businessPhones"]!.AsArray().Select(p => p!.GetValue<string>()));
    }

    [Fact]
    public async Task Idempotent_calls_are_retried_once_after_a_timeout()
    {
        var graph = new ScriptedGraph(Hang(), Respond(HttpStatusCode.OK, "{}"), Respond(HttpStatusCode.OK, CreatedJson));

        await Service(graph, timeoutSeconds: 1).UpdateAsync("AAMkAD-123", Contact, CancellationToken.None);

        Assert.Equal([HttpMethod.Get, HttpMethod.Get, HttpMethod.Patch], graph.Requests.Select(r => r.Method));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, null, ErrorCodes.GraphAuthorizationFailed)]
    [InlineData(HttpStatusCode.Unauthorized, null, ErrorCodes.GraphAuthorizationFailed)]
    [InlineData(HttpStatusCode.NotFound, "MailboxNotEnabledForRESTAPI", ErrorCodes.GraphAuthorizationFailed)]
    [InlineData(HttpStatusCode.BadRequest, "ErrorInvalidRequest", ErrorCodes.GraphContactCreateFailed)]
    [InlineData(HttpStatusCode.InternalServerError, null, ErrorCodes.GraphUnavailable)]
    public async Task Create_failures_map_to_stable_error_codes(HttpStatusCode status, string? graphCode, string expected)
    {
        var body = graphCode is null ? "{}" : $$$"""{"error":{"code":"{{{graphCode}}}","message":"details"}}""";
        var graph = new ScriptedGraph(Respond(status, body));

        var error = await Assert.ThrowsAsync<AppException>(() => Service(graph).CreateAsync(Contact, CancellationToken.None));

        Assert.Equal(expected, error.Code);
        Assert.DoesNotContain("details", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Updating_a_deleted_contact_reports_not_found()
    {
        var graph = new ScriptedGraph(Respond(HttpStatusCode.NotFound, """{"error":{"code":"ErrorItemNotFound"}}"""));

        var error = await Assert.ThrowsAsync<AppException>(() => Service(graph).UpdateAsync("AAMkAD-gone", Contact, CancellationToken.None));

        Assert.Equal(ErrorCodes.ContactNotFound, error.Code);
    }

    [Theory]
    [InlineData("../../me/messages")]
    [InlineData("id with spaces")]
    [InlineData("")]
    public async Task Malformed_ids_never_reach_graph(string id)
    {
        var graph = new ScriptedGraph();

        var error = await Assert.ThrowsAsync<AppException>(() => Service(graph).UpdateAsync(id, Contact, CancellationToken.None));

        Assert.Equal(ErrorCodes.ContactNotFound, error.Code);
        Assert.Empty(graph.Requests);
    }

    [Fact]
    public async Task Consent_or_conditional_access_problems_are_authorization_failures()
    {
        var graph = new ScriptedGraph();
        var tokens = new FakeTokenProvider { Failure = new MsalUiRequiredException("invalid_grant", "AADSTS65001") };

        var error = await Assert.ThrowsAsync<AppException>(() => Service(graph, tokens: tokens).CreateAsync(Contact, CancellationToken.None));

        Assert.Equal(ErrorCodes.GraphAuthorizationFailed, error.Code);
        Assert.Empty(graph.Requests);
    }

    [Fact]
    public async Task Missing_api_credential_is_an_authorization_failure_not_a_crash()
    {
        var tokens = new FakeTokenProvider { Failure = new MsalClientException("client_credentials_missing", "No credential") };

        var error = await Assert.ThrowsAsync<AppException>(() => Service(new ScriptedGraph(), tokens: tokens).CreateAsync(Contact, CancellationToken.None));

        Assert.Equal(ErrorCodes.GraphAuthorizationFailed, error.Code);
    }

    [Fact]
    public async Task A_hanging_token_endpoint_is_cut_off_by_the_graph_budget()
    {
        var graph = new ScriptedGraph();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var error = await Assert.ThrowsAsync<AppException>(() =>
            Service(graph, timeoutSeconds: 1, tokens: new FakeTokenProvider { Hang = true }).CreateAsync(Contact, CancellationToken.None));

        Assert.Equal(ErrorCodes.GraphUnavailable, error.Code);
        Assert.InRange(stopwatch.ElapsedMilliseconds, 800, 5_000);
        Assert.Empty(graph.Requests);
    }

    [Fact]
    public async Task Graph_calls_are_measured_by_operation_and_outcome()
    {
        using var recorder = new Support.MetricRecorder();
        var graph = new ScriptedGraph(Respond(HttpStatusCode.Created, CreatedJson), Throttled(HttpStatusCode.TooManyRequests, seconds: 60));
        var service = Service(graph, metrics: recorder);

        await service.CreateAsync(Contact, CancellationToken.None);
        await Assert.ThrowsAsync<AppException>(() => service.CreateAsync(Contact, CancellationToken.None));

        Assert.Equal(
            ["success", ErrorCodes.GraphRateLimited],
            recorder.For("businesscard.graph.requests").Select(m => m.Tags["outcome"]));
        Assert.All(recorder.For("businesscard.graph.requests"), m => Assert.Equal("create", m.Tags["operation"]));
    }

    [Fact]
    public async Task Requests_only_the_configured_delegated_scope()
    {
        var tokens = new FakeTokenProvider();

        await Service(new ScriptedGraph(Respond(HttpStatusCode.Created, CreatedJson)), tokens: tokens).CreateAsync(Contact, CancellationToken.None);

        Assert.Equal(["https://graph.microsoft.com/Contacts.ReadWrite"], tokens.RequestedScopes);
    }

    [Fact]
    public async Task Duplicate_search_runs_email_mobile_and_name_queries()
    {
        const string page = """{"value":[{"id":"c1","displayName":"Ahmet Yılmaz","givenName":"Ahmet","surname":"Yılmaz","emailAddresses":[{"address":"ahmet.yilmaz@acme.com.tr"}],"mobilePhone":"+905321234567","businessPhones":[]}]}""";
        var graph = new ScriptedGraph(Respond(HttpStatusCode.OK, page), Respond(HttpStatusCode.OK, """{"value":[]}"""), Respond(HttpStatusCode.OK, """{"value":[]}"""));

        var candidates = await Service(graph).FindCandidatesAsync(Contact, CancellationToken.None);

        Assert.Equal("c1", Assert.Single(candidates).Id);
        Assert.Equal(3, graph.Requests.Count);
        var filters = graph.Requests.Select(r => Uri.UnescapeDataString(new Uri(r.Uri).Query)).ToList();
        Assert.Contains(filters, f => f.Contains("emailAddresses/any(a:a/address eq 'ahmet.yilmaz@acme.com.tr')", StringComparison.Ordinal));
        Assert.Contains(filters, f => f.Contains("mobilePhone eq '+90 532 123 45 67' or mobilePhone eq '+905321234567' or mobilePhone eq '0532 123 45 67'", StringComparison.Ordinal));
        Assert.Contains(filters, f => f.Contains("givenName eq 'Ahmet' and surname eq 'Yılmaz'", StringComparison.Ordinal));
        Assert.All(graph.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
    }

    [Fact]
    public async Task Quotes_in_values_cannot_break_the_filter()
    {
        Assert.Equal("'O''Brien'", GraphContactService.Quote("O'Brien"));

        var graph = new ScriptedGraph(Respond(HttpStatusCode.OK, """{"value":[]}"""));
        await Service(graph).FindCandidatesAsync(new ContactDetails { DisplayName = "x", Emails = ["o'brien@acme.ie"] }, CancellationToken.None);

        Assert.Contains("a/address eq 'o''brien@acme.ie'", Uri.UnescapeDataString(graph.Requests.Single().Uri), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_filter_graph_rejects_is_skipped_not_fatal()
    {
        const string page = """{"value":[{"id":"c2","displayName":"A","emailAddresses":[{"address":"ahmet.yilmaz@acme.com.tr"}]}]}""";
        var contact = Contact with { FirstName = null, LastName = null };
        // Requests run in parallel, so responses are chosen by query rather than by order.
        var graph = new ScriptedGraph(ct => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)), ct => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))
        {
            Router = uri => uri.Contains("mobilePhone eq", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"error":{"code":"ErrorInvalidProperty"}}""") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(page) },
        };

        var candidates = await Service(graph).FindCandidatesAsync(contact, CancellationToken.None);

        Assert.Equal("c2", Assert.Single(candidates).Id);
    }

    [Fact]
    public async Task Throttling_still_fails_the_duplicate_check()
    {
        var graph = new ScriptedGraph(Throttled(HttpStatusCode.TooManyRequests, seconds: 60));

        var error = await Assert.ThrowsAsync<AppException>(() =>
            Service(graph).FindCandidatesAsync(new ContactDetails { DisplayName = "x", Emails = ["a@b.com"] }, CancellationToken.None));

        Assert.Equal(ErrorCodes.GraphRateLimited, error.Code);
    }

    [Fact]
    public async Task Nothing_to_search_for_means_no_graph_call()
    {
        var graph = new ScriptedGraph();

        var candidates = await Service(graph).FindCandidatesAsync(new ContactDetails { DisplayName = "NOVA", CompanyName = "NOVA" }, CancellationToken.None);

        Assert.Empty(candidates);
        Assert.Empty(graph.Requests);
    }

    private static GraphContactService Service(ScriptedGraph graph, int timeoutSeconds = 10, FakeTokenProvider? tokens = null, Support.MetricRecorder? metrics = null) =>
        new(
            new HttpClient(graph) { BaseAddress = new Uri("https://graph.test/v1.0/"), Timeout = Timeout.InfiniteTimeSpan },
            tokens ?? new FakeTokenProvider(),
            Options.Create(new GraphOptions { TimeoutSeconds = timeoutSeconds, MaxInlineRetryDelaySeconds = 2 }),
            (metrics ?? Support.Cards.SharedMetrics.Value).Metrics,
            NullLogger<GraphContactService>.Instance);

    private static Func<CancellationToken, Task<HttpResponseMessage>> Respond(HttpStatusCode status, string json) =>
        _ => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    private static Func<CancellationToken, Task<HttpResponseMessage>> Throttled(HttpStatusCode status, int seconds) =>
        _ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent("{}") };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            return Task.FromResult(response);
        };

    private static Func<CancellationToken, Task<HttpResponseMessage>> Hang() =>
        async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        };

    private sealed record SentRequest(HttpMethod Method, string Uri, string? Authorization, string? ClientRequestId, string Body);

    private sealed class ScriptedGraph(params Func<CancellationToken, Task<HttpResponseMessage>>[] responses) : HttpMessageHandler
    {
        private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _responses = new(responses);
        private readonly object _lock = new();

        public List<SentRequest> Requests { get; } = [];

        /// <summary>When set, responds by URL instead of in order (for parallel requests).</summary>
        public Func<string, HttpResponseMessage>? Router { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var uri = request.RequestUri!.ToString();
            Func<CancellationToken, Task<HttpResponseMessage>> respond;
            lock (_lock)
            {
                Requests.Add(new SentRequest(
                    request.Method,
                    uri,
                    request.Headers.Authorization?.ToString(),
                    request.Headers.TryGetValues("client-request-id", out var ids) ? ids.Single() : null,
                    body));
                respond = Router is { } router
                    ? _ => Task.FromResult(router(Uri.UnescapeDataString(uri)))
                    : _responses.Dequeue();
            }
            return await respond(cancellationToken);
        }
    }

    private sealed class FakeTokenProvider : IAuthorizationHeaderProvider
    {
        public Exception? Failure { get; init; }
        public bool Hang { get; init; }
        public List<string> RequestedScopes { get; } = [];

        public async Task<string> CreateAuthorizationHeaderForUserAsync(
            IEnumerable<string> scopes,
            AuthorizationHeaderProviderOptions? authorizationHeaderProviderOptions = null,
            ClaimsPrincipal? claimsPrincipal = null,
            CancellationToken cancellationToken = default)
        {
            if (Hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            if (Failure is not null)
            {
                throw Failure;
            }
            RequestedScopes.AddRange(scopes);
            return "Bearer obo-token";
        }

        public Task<string> CreateAuthorizationHeaderForAppAsync(
            string scopes,
            AuthorizationHeaderProviderOptions? downstreamApiOptions = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("App-only tokens are never used: contacts are written as the user.");

        public Task<string> CreateAuthorizationHeaderAsync(
            IEnumerable<string> scopes,
            AuthorizationHeaderProviderOptions? options = null,
            ClaimsPrincipal? claimsPrincipal = null,
            CancellationToken cancellationToken = default) =>
            CreateAuthorizationHeaderForUserAsync(scopes, options, claimsPrincipal, cancellationToken);
    }
}
