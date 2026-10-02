using BusinessCard.Domain.Cards;

namespace BusinessCard.Application.BusinessCards.Llm;

/// <summary>
/// Semantic classification of card lines by a language model. The only AI
/// abstraction in the system, so the model or serving stack can be replaced
/// without touching parsing logic.
/// </summary>
/// <remarks>
/// Implementations must not throw for model problems (timeout, unavailable,
/// malformed output): they return <see cref="LlmBusinessCardResult.Failed"/> so
/// the caller can fall back immediately. Only caller cancellation propagates.
/// </remarks>
public interface IBusinessCardLlmClassifier
{
    Task<LlmBusinessCardResult> ClassifyAsync(
        BusinessCardClassificationRequest request,
        CancellationToken cancellationToken);
}

/// <param name="Lines">Lines still needing semantic classification; ids are <see cref="CardLine.Order"/>.</param>
public sealed record BusinessCardClassificationRequest(IReadOnlyList<CardLine> Lines);

/// <summary>Line ids per field, as returned by the model (not yet grounded).</summary>
public sealed record LlmClassification(
    IReadOnlyList<int> Person,
    IReadOnlyList<int> Company,
    IReadOnlyList<int> Title,
    IReadOnlyList<int> Department,
    IReadOnlyList<int> Address,
    string? City,
    string? State,
    string? Country);

public enum LlmStatus
{
    Ok,
    Timeout,
    Unavailable,
    InvalidResponse,
    Disabled,

    /// <summary>No call was needed: every line was handled deterministically.</summary>
    Skipped,
}

public sealed record LlmBusinessCardResult(
    LlmStatus Status,
    LlmClassification? Classification,
    string Model,
    TimeSpan Duration)
{
    public bool Succeeded => Status == LlmStatus.Ok && Classification is not null;

    public static LlmBusinessCardResult Success(LlmClassification classification, string model, TimeSpan duration) =>
        new(LlmStatus.Ok, classification, model, duration);

    public static LlmBusinessCardResult Failed(LlmStatus status, string model, TimeSpan duration) =>
        new(status, null, model, duration);
}
