using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using BusinessCard.Application.BusinessCards.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BusinessCard.Infrastructure.Llm;

/// <summary>
/// Classifies card lines with the self-hosted model via an OpenAI-compatible
/// <c>/v1/chat/completions</c> endpoint (llama.cpp llama-server), using
/// JSON-schema-constrained decoding. Named after the initial model, but nothing here
/// is Phi-specific: Gemma or Qwen behind the same server only need <c>Ai:Model</c>.
/// </summary>
/// <remarks>
/// Latency rules: one attempt, no retries, a hard per-request budget, a pooled
/// keep-alive connection, a short prompt, temperature 0 and a small output cap.
/// Every model-side failure becomes a <see cref="LlmBusinessCardResult"/> so the parse
/// falls back immediately. Prompts and answers contain card PII and are never logged.
/// </remarks>
internal sealed partial class PhiBusinessCardLlmClassifier(
    HttpClient httpClient,
    IOptions<AiOptions> options,
    ILogger<PhiBusinessCardLlmClassifier> logger) : IBusinessCardLlmClassifier
{
    private static readonly JsonSerializerOptions CompletionJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    // The answer must match the schema exactly: unknown or missing keys are malformed.
    private static readonly JsonSerializerOptions AnswerJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    // The default encoder escapes every non-ASCII character ("Yılmaz" → "Yılmaz"),
    // inflating Turkish payloads for nothing; plain UTF-8 is valid JSON for an API body.
    private static readonly JsonWriterOptions RequestWriterOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    private readonly AiOptions _options = options.Value;

    public async Task<LlmBusinessCardResult> ClassifyAsync(
        BusinessCardClassificationRequest request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.TimeoutMilliseconds);

        var userPrompt = PromptContract.FormatLines(request.Lines);
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
            {
                Content = new ByteArrayContent(BuildRequestBody(userPrompt))
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" } },
                },
            };
            if (!string.IsNullOrEmpty(_options.ApiKey))
            {
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            }

            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                LogHttpFailure(logger, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);
                return Failed(LlmStatus.Unavailable, stopwatch);
            }

            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            var completion = await JsonSerializer.DeserializeAsync<ChatCompletion>(body, CompletionJson, timeout.Token);
            var classification = ParseAnswer(completion);
            if (classification is null)
            {
                LogInvalidAnswer(logger, stopwatch.ElapsedMilliseconds);
                return Failed(LlmStatus.InvalidResponse, stopwatch);
            }

            LogSucceeded(
                logger,
                _options.Model,
                stopwatch.ElapsedMilliseconds,
                request.Lines.Count,
                userPrompt.Length,
                completion!.Usage?.PromptTokens,
                completion.Usage?.CompletionTokens);
            return LlmBusinessCardResult.Success(classification, _options.Model, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our budget expired, not the caller's request.
            LogTimeout(logger, _options.Model, _options.TimeoutMilliseconds);
            return Failed(LlmStatus.Timeout, stopwatch);
        }
        catch (HttpRequestException e)
        {
            LogUnreachable(logger, e.HttpRequestError.ToString(), stopwatch.ElapsedMilliseconds);
            return Failed(LlmStatus.Unavailable, stopwatch);
        }
        catch (JsonException)
        {
            LogInvalidAnswer(logger, stopwatch.ElapsedMilliseconds);
            return Failed(LlmStatus.InvalidResponse, stopwatch);
        }
    }

    internal byte[] BuildRequestBody(string userPrompt)
    {
        using var buffer = new MemoryStream(4_096);
        using (var json = new Utf8JsonWriter(buffer, RequestWriterOptions))
        {
            json.WriteStartObject();
            json.WriteString("model", _options.Model);
            json.WriteNumber("temperature", 0);
            json.WriteNumber("max_tokens", _options.MaxOutputTokens);
            json.WriteBoolean("stream", false);
            // Reuse the KV cache of the identical system-prompt prefix (llama.cpp default; explicit for clarity).
            json.WriteBoolean("cache_prompt", true);

            json.WriteStartArray("messages");
            WriteMessage(json, "system", PromptContract.SystemPrompt);
            WriteMessage(json, "user", userPrompt);
            json.WriteEndArray();

            // llama.cpp reads "schema"; OpenAI-style servers read "json_schema.schema".
            // Both are sent so the client works unchanged across OpenAI-compatible servers.
            json.WriteStartObject("response_format");
            json.WriteString("type", "json_schema");
            json.WritePropertyName("schema");
            json.WriteRawValue(PromptContract.OutputSchemaJson, skipInputValidation: true);
            json.WriteStartObject("json_schema");
            json.WriteString("name", "business_card_classification");
            json.WriteBoolean("strict", true);
            json.WritePropertyName("schema");
            json.WriteRawValue(PromptContract.OutputSchemaJson, skipInputValidation: true);
            json.WriteEndObject();
            json.WriteEndObject();

            json.WriteEndObject();
        }
        return buffer.ToArray();
    }

    /// <summary>Null when the completion is truncated, empty or does not match the schema.</summary>
    internal static LlmClassification? ParseAnswer(ChatCompletion? completion)
    {
        var choice = completion?.Choices is { Count: > 0 } choices ? choices[0] : null;
        var content = choice?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content) || choice!.FinishReason == "length")
        {
            return null;
        }

        var answer = JsonSerializer.Deserialize<Answer>(content, AnswerJson);
        return answer is null
            ? null
            : new LlmClassification(answer.Person, answer.Company, answer.Title, answer.Dept, answer.Address, answer.City, answer.State, answer.Country);
    }

    private LlmBusinessCardResult Failed(LlmStatus status, Stopwatch stopwatch) =>
        LlmBusinessCardResult.Failed(status, _options.Model, stopwatch.Elapsed);

    private static void WriteMessage(Utf8JsonWriter json, string role, string content)
    {
        json.WriteStartObject();
        json.WriteString("role", role);
        json.WriteString("content", content);
        json.WriteEndObject();
    }

    internal sealed record ChatCompletion(IReadOnlyList<Choice>? Choices, TokenUsage? Usage);

    internal sealed record Choice(ChoiceMessage? Message, string? FinishReason);

    internal sealed record ChoiceMessage(string? Content);

    internal sealed record TokenUsage(int? PromptTokens, int? CompletionTokens);

    private sealed record Answer(
        [property: JsonRequired] int[] Person,
        [property: JsonRequired] int[] Company,
        [property: JsonRequired] int[] Title,
        [property: JsonRequired] int[] Dept,
        [property: JsonRequired] int[] Address,
        [property: JsonRequired] string? City,
        [property: JsonRequired] string? State,
        [property: JsonRequired] string? Country);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "LLM classification succeeded: {LlmModel} in {LlmDurationMs} ms, {LineCount} lines, {PromptChars} prompt chars, {PromptTokens} prompt tokens, {OutputTokens} output tokens")]
    private static partial void LogSucceeded(ILogger logger, string llmModel, long llmDurationMs, int lineCount, int promptChars, int? promptTokens, int? outputTokens);

    [LoggerMessage(Level = LogLevel.Warning, Message = "LLM timed out: {LlmModel} exceeded {TimeoutMs} ms; using deterministic fallback")]
    private static partial void LogTimeout(ILogger logger, string llmModel, int timeoutMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "LLM returned HTTP {StatusCode} after {LlmDurationMs} ms; using deterministic fallback")]
    private static partial void LogHttpFailure(ILogger logger, int statusCode, long llmDurationMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "LLM unreachable ({HttpRequestError}) after {LlmDurationMs} ms; using deterministic fallback")]
    private static partial void LogUnreachable(ILogger logger, string httpRequestError, long llmDurationMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "LLM answer did not match the schema after {LlmDurationMs} ms; using deterministic fallback")]
    private static partial void LogInvalidAnswer(ILogger logger, long llmDurationMs);
}
