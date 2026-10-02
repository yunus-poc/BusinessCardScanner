using System.Net;
using System.Text;
using BusinessCard.Application.BusinessCards.Llm;
using BusinessCard.Application.Contacts;
using BusinessCard.Domain.Contacts;
using BusinessCard.UnitTests.Support;

namespace BusinessCard.UnitTests.Observability;

public sealed class MetricsTests : IDisposable
{
    private readonly MetricRecorder _recorder = new();

    public void Dispose() => _recorder.Dispose();

    [Fact]
    public async Task Parse_records_duration_and_llm_outcome()
    {
        var llm = new FakeLlmClassifier
        {
            Result = LlmBusinessCardResult.Failed(LlmStatus.Timeout, "phi-4-mini-instruct", TimeSpan.FromMilliseconds(2_500)),
        };

        await Cards.Service(llm, metrics: _recorder.Metrics).ParseAsync(Cards.Request(["Ahmet Yılmaz", "ACME"]), CancellationToken.None);

        var parse = Assert.Single(_recorder.For("businesscard.parse.duration"));
        Assert.Equal("timeout", parse.Tags["llm_status"]);
        var llmDuration = Assert.Single(_recorder.For("businesscard.llm.duration"));
        Assert.Equal(2_500, llmDuration.Value);
        Assert.Equal("phi-4-mini-instruct", llmDuration.Tags["model"]);
        var request = Assert.Single(_recorder.For("businesscard.llm.requests"));
        Assert.Equal("timeout", request.Tags["status"]);
    }

    [Fact]
    public async Task Skipped_llm_calls_are_not_counted_as_requests()
    {
        await Cards.Service(metrics: _recorder.Metrics).ParseAsync(Cards.Request(["info@acme.com"]), CancellationToken.None);

        Assert.Empty(_recorder.For("businesscard.llm.requests"));
        Assert.Equal("skipped", Assert.Single(_recorder.For("businesscard.parse.duration")).Tags["llm_status"]);
    }

    [Fact]
    public async Task Duplicate_checks_are_counted_by_result()
    {
        var service = new ContactsService(new CandidateGraph(), _recorder.Metrics);

        await service.CheckDuplicatesAsync(new SaveContactRequest { Email = "known@acme.com" }, CancellationToken.None);
        await service.CheckDuplicatesAsync(new SaveContactRequest { Email = "new@acme.com" }, CancellationToken.None);

        Assert.Equal([true, false], _recorder.For("businesscard.duplicates.checks").Select(m => m.Tags["found"]));
    }

    [Fact]
    public void Client_timings_are_recorded_with_their_llm_status()
    {
        _recorder.Metrics.ClientScanReported(ocrMilliseconds: 420, ocrToPreviewMilliseconds: 1_350, llmStatus: "ok");

        Assert.Equal(420, Assert.Single(_recorder.For("businesscard.client.ocr.duration")).Value);
        var preview = Assert.Single(_recorder.For("businesscard.client.ocr_to_preview.duration"));
        Assert.Equal(1_350, preview.Value);
        Assert.Equal("ok", preview.Tags["llm_status"]);
    }

    [Fact]
    public void Metric_tags_never_carry_card_data()
    {
        _recorder.Metrics.ClientScanReported(1, 2, "ok");
        _recorder.Metrics.DuplicateChecked(found: true);
        _recorder.Metrics.GraphCompleted("create", "success", TimeSpan.FromMilliseconds(80));

        var allowed = new HashSet<string> { "llm_status", "found", "operation", "outcome", "model", "status" };
        Assert.All(_recorder.Measurements, m => Assert.Subset(allowed, m.Tags.Keys.ToHashSet()));
    }

    private sealed class CandidateGraph : IMicrosoftGraphContactService
    {
        public Task<IReadOnlyList<ExistingContact>> FindCandidatesAsync(ContactDetails contact, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExistingContact>>(
                contact.Emails.Contains("known@acme.com")
                    ? [new ExistingContact { Id = "1", DisplayName = "Known", Emails = ["known@acme.com"] }]
                    : []);

        public Task<SavedContact> CreateAsync(ContactDetails contact, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SavedContact> UpdateAsync(string contactId, ContactDetails contact, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
