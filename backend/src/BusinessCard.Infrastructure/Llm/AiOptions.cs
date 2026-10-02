using Microsoft.Extensions.Options;

namespace BusinessCard.Infrastructure.Llm;

public enum AiProvider
{
    /// <summary>Any OpenAI-compatible server with JSON-schema output (llama.cpp llama-server).</summary>
    OpenAiCompatible,

    /// <summary>No model: every parse uses the deterministic fallback. For development and tests.</summary>
    Disabled,
}

/// <summary>Configuration section "Ai". <see cref="ApiKey"/> must come from a secret store or environment.</summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public AiProvider Provider { get; set; } = AiProvider.OpenAiCompatible;

    /// <summary>Model name sent in requests and logged as LlmModel (llama-server's --alias).</summary>
    public string Model { get; set; } = "phi-4-mini-instruct";

    public Uri? BaseUrl { get; set; }

    public string? ApiKey { get; set; }

    /// <summary>
    /// Whole-request budget. The user is waiting for the preview; on expiry the parse
    /// falls back to deterministic results instead of retrying.
    /// </summary>
    public int TimeoutMilliseconds { get; set; } = 2_500;

    /// <summary>The line-id answer needs ~40–60 tokens; the cap bounds worst-case latency.</summary>
    public int MaxOutputTokens { get; set; } = 128;
}

internal sealed class AiOptionsValidator : IValidateOptions<AiOptions>
{
    public ValidateOptionsResult Validate(string? name, AiOptions options)
    {
        if (options.Provider == AiProvider.Disabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (options.BaseUrl is not { IsAbsoluteUri: true } url || url.Scheme is not ("http" or "https"))
        {
            failures.Add("Ai:BaseUrl must be an absolute http(s) URL of the inference service.");
        }
        if (string.IsNullOrWhiteSpace(options.Model))
        {
            failures.Add("Ai:Model is required.");
        }
        if (options.TimeoutMilliseconds is < 200 or > 30_000)
        {
            failures.Add("Ai:TimeoutMilliseconds must be between 200 and 30000.");
        }
        if (options.MaxOutputTokens is < 32 or > 1_024)
        {
            failures.Add("Ai:MaxOutputTokens must be between 32 and 1024.");
        }
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
