using FluentValidation;

namespace BusinessCard.Application.Observability;

/// <summary>
/// On-device timings the server cannot measure itself. Numbers and a status only: the
/// app never sends card content here.
/// </summary>
public sealed class ScanTelemetryRequest
{
    /// <summary>ML Kit recognition time.</summary>
    public int OcrMs { get; init; }

    /// <summary>OCR finished → editable preview shown (includes network and server parse).</summary>
    public int OcrToPreviewMs { get; init; }

    /// <summary>meta.llmStatus of the parse that produced the preview.</summary>
    public string? LlmStatus { get; init; }
}

internal sealed class ScanTelemetryRequestValidator : AbstractValidator<ScanTelemetryRequest>
{
    // A closed set keeps metric tags low-cardinality and free of arbitrary client text.
    private static readonly HashSet<string> KnownStatuses = ["ok", "timeout", "unavailable", "invalidResponse", "disabled", "skipped", "unknown"];

    public ScanTelemetryRequestValidator()
    {
        RuleFor(x => x.OcrMs).InclusiveBetween(0, 60_000);
        RuleFor(x => x.OcrToPreviewMs).InclusiveBetween(0, 120_000);
        RuleFor(x => x.LlmStatus).NotEmpty().Must(s => KnownStatuses.Contains(s!)).WithMessage("Unknown LLM status.");
    }
}
