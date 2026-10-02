namespace BusinessCard.Application.BusinessCards;

/// <summary>Configuration section "Parsing".</summary>
public sealed class ParsingOptions
{
    public const string SectionName = "Parsing";

    /// <summary>
    /// Fields scoring below this are flagged <c>needsReview</c>. Tune against real
    /// cards: higher means more fields highlighted, fewer unnoticed errors.
    /// </summary>
    public double ReviewThreshold { get; set; } = 0.80;
}
