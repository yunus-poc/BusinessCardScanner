using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BusinessCard.Application.BusinessCards.Llm;
using BusinessCard.Domain.Cards;
using BusinessCard.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BusinessCard.UnitTests.Llm;

public sealed class PhiBusinessCardLlmClassifierTests
{
    private const string ValidAnswer =
        """{"person":[2],"company":[1],"title":[3],"dept":[],"address":[8,9],"city":"İstanbul","state":null,"country":null}""";

    private static readonly BusinessCardClassificationRequest Request = new(
    [
        new CardLine("ACME TECHNOLOGY", 1, LayoutRegion.Top, LayoutSize.Large, LayoutAlign.Center, false, LayoutGap.None),
        new CardLine("Ahmet Yılmaz", 2, LayoutRegion.Center, LayoutSize.Large, LayoutAlign.Center, false, LayoutGap.Large),
        new CardLine("Enterprise Solutions Manager", 3, LayoutRegion.Center, LayoutSize.Normal, LayoutAlign.Center, true, LayoutGap.None),
    ]);

    [Fact]
    public async Task Sends_short_deterministic_schema_constrained_request()
    {
        var handler = new StubHandler(_ => Completion(ValidAnswer));

        await Classifier(handler).ClassifyAsync(Request, CancellationToken.None);

        var sent = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("http://llm.internal:8080/v1/chat/completions", sent.Uri.ToString());
        Assert.Equal("Bearer test-key", sent.Authorization);

        var body = JsonNode.Parse(sent.Body)!;
        Assert.Equal("phi-4-mini-instruct", body["model"]!.GetValue<string>());
        Assert.Equal(0, body["temperature"]!.GetValue<int>());
        Assert.Equal(128, body["max_tokens"]!.GetValue<int>());
        Assert.False(body["stream"]!.GetValue<bool>());

        var messages = body["messages"]!.AsArray();
        Assert.Equal("system", messages[0]!["role"]!.GetValue<string>());
        Assert.Equal(PromptContract.SystemPrompt, messages[0]!["content"]!.GetValue<string>());
        Assert.Equal(
            "1 [TOP][LARGE] ACME TECHNOLOGY\n2 [CENTER][LARGE] Ahmet Yılmaz\n3 [CENTER][NORMAL] Enterprise Solutions Manager\n",
            messages[1]!["content"]!.GetValue<string>());

        var format = body["response_format"]!;
        Assert.Equal("json_schema", format["type"]!.GetValue<string>());
        Assert.Contains("person", format["schema"]!["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(format["schema"]!.ToJsonString(), format["json_schema"]!["schema"]!.ToJsonString());
    }

    [Fact]
    public async Task Request_body_is_utf8_so_turkish_text_survives()
    {
        var handler = new StubHandler(_ => Completion(ValidAnswer));

        await Classifier(handler).ClassifyAsync(Request, CancellationToken.None);

        Assert.Contains("Ahmet Yılmaz", handler.Requests.Single().Body, StringComparison.Ordinal);
        Assert.Equal("utf-8", handler.Requests.Single().CharSet);
    }

    [Fact]
    public async Task Maps_a_valid_answer()
    {
        var result = await Classifier(new StubHandler(_ => Completion(ValidAnswer))).ClassifyAsync(Request, CancellationToken.None);

        Assert.True(result.Succeeded);
        var c = result.Classification!;
        Assert.Equal([2], c.Person);
        Assert.Equal([1], c.Company);
        Assert.Equal([3], c.Title);
        Assert.Empty(c.Department);
        Assert.Equal([8, 9], c.Address);
        Assert.Equal("İstanbul", c.City);
        Assert.Null(c.Country);
        Assert.Equal("phi-4-mini-instruct", result.Model);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"person":[2]}""")]
    [InlineData("""{"person":[2],"company":[1],"title":[3],"dept":[],"address":[],"city":null,"state":null,"country":null,"notes":"x"}""")]
    [InlineData("""{"person":"Ahmet","company":[1],"title":[3],"dept":[],"address":[],"city":null,"state":null,"country":null}""")]
    [InlineData("")]
    public async Task Malformed_answers_are_invalid_responses(string content)
    {
        var result = await Classifier(new StubHandler(_ => Completion(content))).ClassifyAsync(Request, CancellationToken.None);

        Assert.Equal(LlmStatus.InvalidResponse, result.Status);
        Assert.Null(result.Classification);
    }

    [Fact]
    public async Task Truncated_answer_is_invalid_even_if_it_parses()
    {
        var result = await Classifier(new StubHandler(_ => Completion(ValidAnswer, finishReason: "length")))
            .ClassifyAsync(Request, CancellationToken.None);

        Assert.Equal(LlmStatus.InvalidResponse, result.Status);
    }

    [Theory]
    [InlineData("""{"choices":[]}""")]
    [InlineData("""{"error":"oops"}""")]
    [InlineData("<html>gateway</html>")]
    public async Task Unexpected_envelopes_are_invalid_responses(string body)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });

        var result = await Classifier(handler).ClassifyAsync(Request, CancellationToken.None);

        Assert.Equal(LlmStatus.InvalidResponse, result.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Error_statuses_mean_unavailable(HttpStatusCode status)
    {
        var result = await Classifier(new StubHandler(_ => new HttpResponseMessage(status)))
            .ClassifyAsync(Request, CancellationToken.None);

        Assert.Equal(LlmStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task Connection_failure_means_unavailable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused"));

        var result = await Classifier(handler).ClassifyAsync(Request, CancellationToken.None);

        Assert.Equal(LlmStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task Slow_model_times_out_quickly_without_retrying()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Completion(ValidAnswer);
        });
        var stopwatch = Stopwatch.StartNew();

        var result = await Classifier(handler, timeoutMs: 200).ClassifyAsync(Request, CancellationToken.None);

        Assert.Equal(LlmStatus.Timeout, result.Status);
        Assert.InRange(stopwatch.ElapsedMilliseconds, 150, 2_000);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_instead_of_reporting_a_timeout()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Completion(ValidAnswer);
        });
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Classifier(handler, timeoutMs: 5_000).ClassifyAsync(Request, caller.Token));
    }

    [Fact]
    public async Task No_api_key_means_no_authorization_header()
    {
        var handler = new StubHandler(_ => Completion(ValidAnswer));

        await Classifier(handler, apiKey: null).ClassifyAsync(Request, CancellationToken.None);

        Assert.Null(handler.Requests.Single().Authorization);
    }

    [Fact]
    public void Embedded_prompt_and_schema_are_the_shared_contract_files()
    {
        var contract = Path.Combine(RepoRoot(), "inference", "contract");

        Assert.Equal(File.ReadAllText(Path.Combine(contract, "system-prompt.txt")), PromptContract.SystemPrompt);
        Assert.Equal(File.ReadAllText(Path.Combine(contract, "classification.schema.json")), PromptContract.OutputSchemaJson);
        Assert.NotNull(JsonNode.Parse(PromptContract.OutputSchemaJson));
    }

    private static PhiBusinessCardLlmClassifier Classifier(StubHandler handler, int timeoutMs = 2_500, string? apiKey = "test-key") =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://llm.internal:8080/"), Timeout = Timeout.InfiniteTimeSpan },
            Options.Create(new AiOptions
            {
                BaseUrl = new Uri("http://llm.internal:8080"),
                ApiKey = apiKey,
                TimeoutMilliseconds = timeoutMs,
            }),
            NullLogger<PhiBusinessCardLlmClassifier>.Instance);

    private static HttpResponseMessage Completion(string content, string finishReason = "stop")
    {
        var envelope = new
        {
            choices = new[] { new { message = new { role = "assistant", content }, finish_reason = finishReason } },
            usage = new { prompt_tokens = 210, completion_tokens = 41 },
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
        };
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "inference", "contract")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed record SentRequest(HttpMethod Method, Uri Uri, string? Authorization, string Body, string? CharSet);

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this((request, _) => Task.FromResult(respond(request)))
        {
        }

        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new SentRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                body,
                request.Content?.Headers.ContentType?.CharSet));
            return await respond(request, cancellationToken);
        }
    }
}
