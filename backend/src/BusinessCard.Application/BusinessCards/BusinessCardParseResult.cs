namespace BusinessCard.Application.BusinessCards;

/// <summary>
/// One editable field. <see cref="Confidence"/> is a heuristic score from several
/// signals (validation, layout, cross-field checks), not a calibrated probability.
/// </summary>
public sealed record ParsedField(string? Value, double Confidence, bool NeedsReview)
{
    public static ParsedField Empty { get; } = new(null, 0, false);
}

public sealed class ParsedContact
{
    public ParsedField FirstName { get; init; } = ParsedField.Empty;
    public ParsedField LastName { get; init; } = ParsedField.Empty;
    public ParsedField DisplayName { get; init; } = ParsedField.Empty;
    public ParsedField CompanyName { get; init; } = ParsedField.Empty;
    public ParsedField JobTitle { get; init; } = ParsedField.Empty;
    public ParsedField Department { get; init; } = ParsedField.Empty;
    public ParsedField Email { get; init; } = ParsedField.Empty;
    public ParsedField MobilePhone { get; init; } = ParsedField.Empty;
    public ParsedField BusinessPhone { get; init; } = ParsedField.Empty;
    public ParsedField Website { get; init; } = ParsedField.Empty;
    public ParsedField Street { get; init; } = ParsedField.Empty;
    public ParsedField City { get; init; } = ParsedField.Empty;
    public ParsedField State { get; init; } = ParsedField.Empty;
    public ParsedField PostalCode { get; init; } = ParsedField.Empty;
    public ParsedField Country { get; init; } = ParsedField.Empty;
    public ParsedField Notes { get; init; } = ParsedField.Empty;
}

/// <param name="LlmUsed">The model's classification contributed to the result.</param>
/// <param name="FallbackUsed">The model was needed but failed; semantic fields come from fallback logic and need review.</param>
/// <param name="LlmStatus">Diagnostic only (ok, timeout, unavailable, invalidResponse, disabled, skipped).</param>
public sealed record ParseMetadata(bool LlmUsed, bool FallbackUsed, Llm.LlmStatus LlmStatus);

public sealed record BusinessCardParseResult(
    ParsedContact Contact,
    // Cards often carry a second email or phone; Graph can store up to three
    // emails and several business phones, so they are offered, not dropped.
    IReadOnlyList<string> AdditionalEmails,
    IReadOnlyList<string> AdditionalPhones,
    ParseMetadata Meta);
