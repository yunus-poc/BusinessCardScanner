using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BusinessCard.Application.BusinessCards.Llm;
using BusinessCard.Infrastructure.Llm;
using BusinessCard.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessCard.IntegrationTests;

public sealed class LlmIntegrationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Endpoint = "/api/business-cards/parse";

    private static readonly object Card = new
    {
        lines = new object[]
        {
            new { text = "ACME TECHNOLOGY", order = 1, region = "top", size = "large" },
            new { text = "Ahmet Yılmaz", order = 2, region = "center", size = "large" },
            new { text = "Enterprise Solutions Manager", order = 3, region = "center", size = "normal" },
            new { text = "ahmet.yilmaz@acme.com.tr", order = 4, region = "bottom", size = "small" },
        },
    };

    [Fact]
    public async Task Disabled_provider_parses_deterministically()
    {
        var meta = (await Parse(factory)).GetProperty("meta");

        Assert.Equal("disabled", meta.GetProperty("llmStatus").GetString());
        Assert.True(meta.GetProperty("fallbackUsed").GetBoolean());
    }

    [Theory]
    [InlineData(LlmStatus.Timeout, "timeout")]
    [InlineData(LlmStatus.Unavailable, "unavailable")]
    [InlineData(LlmStatus.InvalidResponse, "invalidResponse")]
    public async Task Llm_failures_never_fail_the_scan(LlmStatus failure, string wireStatus)
    {
        using var app = WithClassifier(new StaticClassifier(LlmBusinessCardResult.Failed(failure, "phi-4-mini-instruct", TimeSpan.FromSeconds(2.5))));

        var body = await Parse(app);

        Assert.Equal("ahmet.yilmaz@acme.com.tr", body.GetProperty("contact").GetProperty("email").GetProperty("value").GetString());
        var meta = body.GetProperty("meta");
        Assert.Equal(wireStatus, meta.GetProperty("llmStatus").GetString());
        Assert.True(meta.GetProperty("fallbackUsed").GetBoolean());
        Assert.False(meta.GetProperty("llmUsed").GetBoolean());
    }

    [Fact]
    public async Task OpenAi_compatible_answer_flows_through_the_real_client_into_the_response()
    {
        var stub = new CompletionStub("""{"person":[2],"company":[1],"title":[3],"dept":[],"address":[],"city":null,"state":null,"country":null}""");
        using var app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Ai:Provider", "OpenAiCompatible");
            builder.UseSetting("Ai:BaseUrl", "http://llm.test:8080");
            builder.UseSetting("Ai:ApiKey", "integration-key");
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<IBusinessCardLlmClassifier, PhiBusinessCardLlmClassifier>()
                    .ConfigurePrimaryHttpMessageHandler(() => stub));
        });

        var body = await Parse(app);

        var contact = body.GetProperty("contact");
        Assert.Equal("Ahmet", contact.GetProperty("firstName").GetProperty("value").GetString());
        Assert.Equal("Yılmaz", contact.GetProperty("lastName").GetProperty("value").GetString());
        Assert.Equal("ACME Technology", contact.GetProperty("companyName").GetProperty("value").GetString());
        Assert.Equal("ok", body.GetProperty("meta").GetProperty("llmStatus").GetString());

        // The email line was consumed deterministically and never sent to the model.
        Assert.DoesNotContain("ahmet.yilmaz@acme.com.tr", stub.LastBody, StringComparison.Ordinal);
        Assert.Equal("Bearer integration-key", stub.LastAuthorization);
        Assert.Equal("http://llm.test:8080/v1/chat/completions", stub.LastUri);
    }

    [Fact]
    public async Task Readiness_reports_an_unreachable_llm_as_degraded_but_stays_up()
    {
        using var app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Ai:Provider", "OpenAiCompatible");
            builder.UseSetting("Ai:BaseUrl", "http://127.0.0.1:9"); // nothing listens on the discard port
        });

        var response = await app.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Degraded", report.GetProperty("status").GetString());
        var llm = report.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "llm");
        Assert.Equal("Degraded", llm.GetProperty("status").GetString());
    }

    [Fact]
    public void Invalid_ai_configuration_fails_at_startup()
    {
        using var app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Ai:Provider", "OpenAiCompatible");
            builder.UseSetting("Ai:BaseUrl", "not-a-url");
        });

        var error = Assert.ThrowsAny<Exception>(() => app.CreateClient());
        Assert.Contains("Ai:BaseUrl", error.ToString(), StringComparison.Ordinal);
    }

    private static async Task<JsonElement> Parse(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.Create());
        var response = await client.PostAsJsonAsync(Endpoint, Card);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> WithClassifier(IBusinessCardLlmClassifier classifier) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton(classifier)));

    private sealed class StaticClassifier(LlmBusinessCardResult result) : IBusinessCardLlmClassifier
    {
        public Task<LlmBusinessCardResult> ClassifyAsync(BusinessCardClassificationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    /// <summary>Plays an OpenAI-compatible server returning a fixed answer.</summary>
    private sealed class CompletionStub(string answer) : HttpMessageHandler
    {
        public string LastBody { get; private set; } = string.Empty;
        public string? LastAuthorization { get; private set; }
        public string? LastUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastUri = request.RequestUri?.ToString();
            var envelope = JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content = answer }, finish_reason = "stop" } },
                usage = new { prompt_tokens = 200, completion_tokens = 40 },
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(envelope, Encoding.UTF8, "application/json"),
            };
        }
    }
}
