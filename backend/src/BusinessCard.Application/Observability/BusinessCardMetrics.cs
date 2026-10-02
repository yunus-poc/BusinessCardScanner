using System.Diagnostics;
using System.Diagnostics.Metrics;
using BusinessCard.Application.BusinessCards.Llm;

namespace BusinessCard.Application.Observability;

/// <summary>
/// Operational metrics. Tags are low-cardinality categories only (statuses, operations,
/// model name): never user, card or contact data. p50/p95 come from the histograms.
/// </summary>
public sealed class BusinessCardMetrics
{
    public const string MeterName = "BusinessCard";

    private readonly Histogram<double> _parseDuration;
    private readonly Histogram<double> _llmDuration;
    private readonly Counter<long> _llmRequests;
    private readonly Histogram<double> _graphDuration;
    private readonly Counter<long> _graphRequests;
    private readonly Counter<long> _duplicateChecks;
    private readonly Histogram<double> _clientOcrDuration;
    private readonly Histogram<double> _clientOcrToPreview;

    public BusinessCardMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _parseDuration = meter.CreateHistogram<double>("businesscard.parse.duration", "ms", "Server-side parse time");
        _llmDuration = meter.CreateHistogram<double>("businesscard.llm.duration", "ms", "LLM classification time, including timeouts");
        _llmRequests = meter.CreateCounter<long>("businesscard.llm.requests", description: "LLM calls by outcome (timeout and fallback rates)");
        _graphDuration = meter.CreateHistogram<double>("businesscard.graph.duration", "ms", "Microsoft Graph call time, including retries");
        _graphRequests = meter.CreateCounter<long>("businesscard.graph.requests", description: "Microsoft Graph calls by operation and outcome");
        _duplicateChecks = meter.CreateCounter<long>("businesscard.duplicates.checks", description: "Duplicate checks by result");
        _clientOcrDuration = meter.CreateHistogram<double>("businesscard.client.ocr.duration", "ms", "On-device OCR time reported by the app");
        _clientOcrToPreview = meter.CreateHistogram<double>("businesscard.client.ocr_to_preview.duration", "ms", "OCR finished → editable preview shown, reported by the app");
    }

    public void ParseCompleted(TimeSpan duration, LlmStatus llmStatus) =>
        _parseDuration.Record(duration.TotalMilliseconds, Tag("llm_status", llmStatus));

    /// <summary>Not recorded for <see cref="LlmStatus.Skipped"/>: no call was made.</summary>
    public void LlmCompleted(LlmBusinessCardResult result)
    {
        if (result.Status == LlmStatus.Skipped)
        {
            return;
        }
        var tags = new TagList { { "model", result.Model }, { "status", Status(result.Status) } };
        _llmDuration.Record(result.Duration.TotalMilliseconds, tags);
        _llmRequests.Add(1, tags);
    }

    /// <param name="outcome">"success" or the error code, e.g. "GRAPH_RATE_LIMITED".</param>
    public void GraphCompleted(string operation, string outcome, TimeSpan duration)
    {
        var tags = new TagList { { "operation", operation }, { "outcome", outcome } };
        _graphDuration.Record(duration.TotalMilliseconds, tags);
        _graphRequests.Add(1, tags);
    }

    public void DuplicateChecked(bool found) => _duplicateChecks.Add(1, new KeyValuePair<string, object?>("found", found));

    public void ClientScanReported(int ocrMilliseconds, int ocrToPreviewMilliseconds, string llmStatus)
    {
        var tag = new KeyValuePair<string, object?>("llm_status", llmStatus);
        _clientOcrDuration.Record(ocrMilliseconds, tag);
        _clientOcrToPreview.Record(ocrToPreviewMilliseconds, tag);
    }

    private static KeyValuePair<string, object?> Tag(string name, LlmStatus status) => new(name, Status(status));

    // Same spelling as the API's meta.llmStatus.
    private static string Status(LlmStatus status) => status switch
    {
        LlmStatus.Ok => "ok",
        LlmStatus.Timeout => "timeout",
        LlmStatus.Unavailable => "unavailable",
        LlmStatus.InvalidResponse => "invalidResponse",
        LlmStatus.Disabled => "disabled",
        LlmStatus.Skipped => "skipped",
        _ => "unknown",
    };
}
