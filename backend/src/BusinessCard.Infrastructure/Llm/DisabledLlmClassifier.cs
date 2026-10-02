using BusinessCard.Application.BusinessCards.Llm;

namespace BusinessCard.Infrastructure.Llm;

/// <summary>Ai:Provider = Disabled: no model, parsing runs deterministically.</summary>
internal sealed class DisabledLlmClassifier : IBusinessCardLlmClassifier
{
    public Task<LlmBusinessCardResult> ClassifyAsync(BusinessCardClassificationRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(LlmBusinessCardResult.Failed(LlmStatus.Disabled, "none", TimeSpan.Zero));
}
