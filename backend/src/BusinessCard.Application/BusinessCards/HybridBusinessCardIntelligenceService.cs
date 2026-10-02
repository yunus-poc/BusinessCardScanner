using System.Diagnostics;
using BusinessCard.Application.BusinessCards.Llm;
using BusinessCard.Application.BusinessCards.Merging;
using BusinessCard.Application.BusinessCards.Parsing;
using BusinessCard.Application.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BusinessCard.Application.BusinessCards;

/// <summary>
/// Hybrid parser, in order:
/// 1. sanitize OCR lines and drop noise;
/// 2. extract email, phones, website, postal code deterministically;
/// 3. send the remaining text (residuals) to the LLM, or skip it if nothing is left;
/// 4. ground the answer against what was sent;
/// 5. fall back to layout heuristics if the LLM failed;
/// 6. merge, normalize and score every field.
/// The LLM is never a single point of failure: a model problem still yields an editable result.
/// </summary>
internal sealed partial class HybridBusinessCardIntelligenceService(
    IBusinessCardLlmClassifier classifier,
    IOptions<ParsingOptions> options,
    BusinessCardMetrics metrics,
    ILogger<HybridBusinessCardIntelligenceService> logger) : IBusinessCardIntelligenceService
{
    public async Task<BusinessCardParseResult> ParseAsync(BusinessCardParseRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var lines = CardLineSanitizer.Sanitize(request.Lines!);
        var extraction = DeterministicExtractor.Extract(lines);
        var context = CardContext.From(lines, extraction);

        // Residual text only, and only lines that still carry something beyond contact data.
        var semanticLines = lines
            .Where(l => !extraction.ConsumedLineOrders.Contains(l.Order))
            .Select(l => l with { Text = extraction.Residuals[l.Order] })
            .Where(l => l.Text.Length > 0)
            .ToList();

        var llm = semanticLines.Count == 0
            ? LlmBusinessCardResult.Failed(LlmStatus.Skipped, "none", TimeSpan.Zero)
            : await classifier.ClassifyAsync(new BusinessCardClassificationRequest(semanticLines), cancellationToken);
        metrics.LlmCompleted(llm);
        var llmCandidates = llm.Succeeded
            ? SemanticCandidates.FromLlm(LlmClassificationGrounding.Ground(llm.Classification!, semanticLines))
            : null;
        var layoutCandidates = LayoutHeuristics.Classify(semanticLines, context);

        var assembled = ContactAssembler.Assemble(
            extraction,
            semanticLines,
            llmCandidates,
            layoutCandidates,
            context,
            options.Value.ReviewThreshold);

        var llmUsed = llm.Status == LlmStatus.Ok;
        var fallbackUsed = !llmUsed && llm.Status != LlmStatus.Skipped;
        var reviewCount = CountNeedingReview(assembled.Contact);

        // Counts, statuses and timings only: never text, names, numbers or addresses.
        LogParsed(
            logger,
            request.Lines!.Count,
            semanticLines.Count,
            llm.Status,
            llm.Model,
            (long)llm.Duration.TotalMilliseconds,
            llmCandidates?.Corrections ?? 0,
            fallbackUsed,
            reviewCount,
            stopwatch.ElapsedMilliseconds);
        metrics.ParseCompleted(stopwatch.Elapsed, llm.Status);

        return new BusinessCardParseResult(
            assembled.Contact,
            assembled.AdditionalEmails,
            assembled.AdditionalPhones,
            new ParseMetadata(llmUsed, fallbackUsed, llm.Status));
    }

    private static int CountNeedingReview(ParsedContact c) =>
        new[]
        {
            c.FirstName, c.LastName, c.CompanyName, c.JobTitle, c.Department, c.Email, c.MobilePhone, c.BusinessPhone,
            c.Website, c.Street, c.City, c.State, c.PostalCode, c.Country,
        }.Count(f => f.NeedsReview);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Parsed business card: {InputLineCount} lines, {SemanticLineCount} sent to LLM, LLM {LlmStatus} ({LlmModel}, {LlmDurationMs} ms, {GroundingCorrections} corrections), ParserFallbackUsed {ParserFallbackUsed}, {ReviewFieldCount} fields need review, total {DurationMs} ms")]
    private static partial void LogParsed(
        ILogger logger,
        int inputLineCount,
        int semanticLineCount,
        LlmStatus llmStatus,
        string llmModel,
        long llmDurationMs,
        int groundingCorrections,
        bool parserFallbackUsed,
        int reviewFieldCount,
        long durationMs);
}
