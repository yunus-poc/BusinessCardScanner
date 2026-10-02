using BusinessCard.Application.BusinessCards.Llm;

namespace BusinessCard.UnitTests.Support;

/// <summary>Scriptable classifier: returns <see cref="Result"/> and records what it was asked.</summary>
internal sealed class FakeLlmClassifier : IBusinessCardLlmClassifier
{
    public LlmBusinessCardResult Result { get; set; } =
        LlmBusinessCardResult.Failed(LlmStatus.Disabled, "fake", TimeSpan.Zero);

    public List<BusinessCardClassificationRequest> Requests { get; } = [];

    public Task<LlmBusinessCardResult> ClassifyAsync(BusinessCardClassificationRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(Result);
    }

    public static LlmBusinessCardResult Answer(
        int[]? person = null,
        int[]? company = null,
        int[]? title = null,
        int[]? dept = null,
        int[]? address = null,
        string? city = null,
        string? state = null,
        string? country = null) =>
        LlmBusinessCardResult.Success(
            new LlmClassification(person ?? [], company ?? [], title ?? [], dept ?? [], address ?? [], city, state, country),
            "fake-model",
            TimeSpan.FromMilliseconds(400));
}
