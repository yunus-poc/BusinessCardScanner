using BusinessCard.Application.BusinessCards.Llm;

namespace BusinessCard.Application.BusinessCards.Merging;

internal enum CandidateSource
{
    /// <summary>Grounded LLM classification.</summary>
    Llm,

    /// <summary>Deterministic layout heuristics, used when the LLM is unavailable.</summary>
    Layout,
}

/// <summary>Which lines hold which semantic field, from either source.</summary>
internal sealed record SemanticCandidates(
    CandidateSource Source,
    IReadOnlyList<int> Person,
    IReadOnlyList<int> Company,
    IReadOnlyList<int> Title,
    IReadOnlyList<int> Department,
    IReadOnlyList<int> Address,
    string? City,
    string? State,
    string? Country,
    int Corrections)
{
    public static SemanticCandidates FromLlm(GroundedClassification grounded) => new(
        CandidateSource.Llm,
        grounded.Classification.Person,
        grounded.Classification.Company,
        grounded.Classification.Title,
        grounded.Classification.Department,
        grounded.Classification.Address,
        grounded.Classification.City,
        grounded.Classification.State,
        grounded.Classification.Country,
        grounded.Corrections);
}
