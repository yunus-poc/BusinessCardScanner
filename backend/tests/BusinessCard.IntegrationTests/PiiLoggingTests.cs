using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BusinessCard.Application.BusinessCards.Llm;
using BusinessCard.Application.Contacts;
using BusinessCard.Infrastructure.Graph;
using BusinessCard.Infrastructure.Llm;
using BusinessCard.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Abstractions;
using Serilog.Core;
using Serilog.Events;

namespace BusinessCard.IntegrationTests;

/// <summary>
/// Runs the whole backend (sanitizer, parser, real LLM client, real Graph client, controllers,
/// error handling) with only the HTTP boundaries stubbed, using the production log
/// configuration with the application's own logs at Debug, and checks that no card or
/// contact data and no credential reaches any log event.
/// </summary>
public sealed class PiiLoggingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string LlmKey = "llm-secret-key";
    private const string OboToken = "obo-secret-token";

    private static readonly string[] Forbidden =
    [
        "Ahmet", "Yılmaz", "Yilmaz", "yilmaz", "ACME", "Acme", "acme", "5321234567", "123 45 67",
        "Büyükdere", "Maslak", "34485", "Sarıyer", "@", LlmKey, OboToken,
    ];

    private static readonly object Card = new
    {
        lines = new object[]
        {
            new { text = "ACME TECHNOLOGY", order = 1, region = "top", size = "large" },
            new { text = "Ahmet Yılmaz", order = 2, region = "center", size = "large" },
            new { text = "ahmet.yilmaz@acme.com.tr", order = 3, region = "bottom", size = "small" },
            new { text = "M: 0532 123 45 67", order = 4, region = "bottom", size = "small" },
            new { text = "Maslak Mah. Büyükdere Cad. No:255", order = 5, region = "bottom", size = "small" },
            new { text = "34485 Sarıyer/İstanbul", order = 6, region = "bottom", size = "small" },
        },
    };

    private static readonly object Contact = new
    {
        firstName = "Ahmet",
        lastName = "Yılmaz",
        companyName = "ACME Technology",
        email = "ahmet.yilmaz@acme.com.tr",
        mobilePhone = "+90 532 123 45 67",
        street = "Maslak Mah. Büyükdere Cad. No:255",
        postalCode = "34485",
    };

    [Fact]
    public async Task No_personal_data_or_secret_reaches_the_logs()
    {
        var sink = new CapturingSink();
        var graph = new GraphStub();
        using var app = factory.WithWebHostBuilder(builder =>
        {
            // Production levels (appsettings.json) with the application's own logging turned up.
            builder.UseSetting("Serilog:MinimumLevel:Default", "Information");
            builder.UseSetting("Serilog:MinimumLevel:Override:BusinessCard", "Debug");
            builder.UseSetting("Ai:Provider", "OpenAiCompatible");
            builder.UseSetting("Ai:BaseUrl", "http://llm.test:8080");
            builder.UseSetting("Ai:ApiKey", LlmKey);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ILogEventSink>(sink);
                services.AddHttpClient<IBusinessCardLlmClassifier, PhiBusinessCardLlmClassifier>()
                    .ConfigurePrimaryHttpMessageHandler(() => new LlmStub());
                services.AddHttpClient<IMicrosoftGraphContactService, GraphContactService>()
                    .ConfigurePrimaryHttpMessageHandler(() => graph);
                services.AddSingleton<IAuthorizationHeaderProvider>(new StaticTokenProvider());
            });
        });
        var client = app.CreateClient();
        var userToken = TestTokens.Create();
        client.DefaultRequestHeaders.Authorization = new("Bearer", userToken);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/business-cards/parse", Card)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/contacts/check-duplicate", Contact)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/contacts", Contact)).StatusCode);
        graph.RejectCreates = true; // Graph's error message echoes the contact: it must not be logged.
        Assert.Equal(HttpStatusCode.BadGateway, (await client.PostAsJsonAsync("/api/contacts", Contact)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/contacts", new { email = "ahmet.yilmaz@" })).StatusCode);

        // Guard against a vacuous pass: the interesting log lines were actually captured.
        var rendered = sink.Events.Select(Render).ToList();
        Assert.Contains(rendered, line => line.Contains("Parsed business card", StringComparison.Ordinal));
        Assert.Contains(rendered, line => line.Contains("LLM classification succeeded", StringComparison.Ordinal));
        Assert.Contains(rendered, line => line.Contains("Graph Create failed", StringComparison.Ordinal));

        var leaks = rendered
            .SelectMany(line => Forbidden.Append(userToken).Where(f => line.Contains(f, StringComparison.Ordinal)).Select(f => (f, line)))
            .ToList();
        Assert.True(leaks.Count == 0, "Personal data in logs:\n" + string.Join('\n', leaks.Select(l => $"[{l.f}] {l.line}")));
    }

    private static string Render(LogEvent e) =>
        string.Join(" | ", new[] { e.RenderMessage() }
            .Concat(e.Properties.Select(p => $"{p.Key}={p.Value}"))
            .Append(e.Exception?.ToString() ?? string.Empty));

    private sealed class CapturingSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }

    private sealed class LlmStub : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            const string answer = """{"person":[2],"company":[1],"title":[],"dept":[],"address":[5,6],"city":"İstanbul","state":null,"country":null}""";
            var body = JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content = answer }, finish_reason = "stop" } },
                usage = new { prompt_tokens = 180, completion_tokens = 40 },
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class GraphStub : HttpMessageHandler
    {
        public bool RejectCreates { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, """{"value":[]}""");
            }
            return RejectCreates
                ? Json(HttpStatusCode.BadRequest, """{"error":{"code":"ErrorInvalidRequest","message":"Contact 'Ahmet Yılmaz' <ahmet.yilmaz@acme.com.tr> is invalid"}}""")
                : Json(HttpStatusCode.Created, """{"id":"AAMkAD-1","displayName":"Ahmet Yılmaz"}""");
        }

        private static Task<HttpResponseMessage> Json(HttpStatusCode status, string body) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private sealed class StaticTokenProvider : IAuthorizationHeaderProvider
    {
        public Task<string> CreateAuthorizationHeaderForUserAsync(
            IEnumerable<string> scopes,
            AuthorizationHeaderProviderOptions? authorizationHeaderProviderOptions = null,
            ClaimsPrincipal? claimsPrincipal = null,
            CancellationToken cancellationToken = default) => Task.FromResult("Bearer " + OboToken);

        public Task<string> CreateAuthorizationHeaderForAppAsync(
            string scopes,
            AuthorizationHeaderProviderOptions? downstreamApiOptions = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> CreateAuthorizationHeaderAsync(
            IEnumerable<string> scopes,
            AuthorizationHeaderProviderOptions? options = null,
            ClaimsPrincipal? claimsPrincipal = null,
            CancellationToken cancellationToken = default) => CreateAuthorizationHeaderForUserAsync(scopes, options, claimsPrincipal, cancellationToken);
    }
}
