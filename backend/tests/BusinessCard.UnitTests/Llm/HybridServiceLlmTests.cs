using BusinessCard.Application.BusinessCards;
using BusinessCard.Application.BusinessCards.Llm;
using BusinessCard.Domain.Cards;
using BusinessCard.UnitTests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace BusinessCard.UnitTests.Llm;

public sealed class HybridServiceLlmTests
{
    private static readonly string[] Card =
    [
        "ACME TECHNOLOGY",                // 1
        "Ahmet Yılmaz",                   // 2
        "Enterprise Solutions Manager",   // 3
        "ahmet.yilmaz@acme.com.tr",       // 4  consumed
        "M: 0532 123 45 67",              // 5  consumed
        "34485 Sarıyer/İstanbul",         // 6
    ];

    private readonly FakeLlmClassifier _llm = new();

    [Fact]
    public async Task Sends_only_lines_not_consumed_deterministically()
    {
        await Parse(Card);

        Assert.Equal([1, 2, 3, 6], _llm.Requests.Single().Lines.Select(l => l.Order));
    }

    [Fact]
    public async Task Skips_the_model_when_nothing_is_left_to_classify()
    {
        var result = await Parse(["info@acme.com", "+90 212 555 00 00"]);

        Assert.Empty(_llm.Requests);
        Assert.Equal(LlmStatus.Skipped, result.Meta.LlmStatus);
        Assert.False(result.Meta.FallbackUsed);
    }

    [Fact]
    public async Task Successful_classification_fills_semantic_fields()
    {
        _llm.Result = FakeLlmClassifier.Answer(person: [2], company: [1], title: [3], address: [6], city: "İstanbul");

        var result = await Parse(Card);

        var contact = result.Contact;
        Assert.Equal("Ahmet Yılmaz", contact.DisplayName.Value);
        Assert.Equal("Ahmet", contact.FirstName.Value);
        Assert.Equal("Yılmaz", contact.LastName.Value);
        Assert.Equal("ACME Technology", contact.CompanyName.Value);
        Assert.Equal("Enterprise Solutions Manager", contact.JobTitle.Value);
        Assert.Equal("İstanbul", contact.City.Value);
        Assert.True(result.Meta.LlmUsed);
        Assert.False(result.Meta.FallbackUsed);
        Assert.Equal(LlmStatus.Ok, result.Meta.LlmStatus);
    }

    [Theory]
    [InlineData(LlmStatus.Timeout)]
    [InlineData(LlmStatus.Unavailable)]
    [InlineData(LlmStatus.InvalidResponse)]
    [InlineData(LlmStatus.Disabled)]
    public async Task Model_failure_still_returns_deterministic_fields_and_flags_fallback(LlmStatus failure)
    {
        _llm.Result = LlmBusinessCardResult.Failed(failure, "phi-4-mini-instruct", TimeSpan.FromMilliseconds(2_500));

        var result = await Parse(Card);

        Assert.Equal("ahmet.yilmaz@acme.com.tr", result.Contact.Email.Value);
        Assert.Equal("+90 532 123 45 67", result.Contact.MobilePhone.Value);
        Assert.Equal("İstanbul", result.Contact.City.Value); // deterministic province hint
        Assert.True(result.Meta.FallbackUsed);
        Assert.False(result.Meta.LlmUsed);
        Assert.Equal(failure, result.Meta.LlmStatus);
    }

    [Fact]
    public async Task Ungrounded_model_claims_never_reach_the_contact()
    {
        _llm.Result = FakeLlmClassifier.Answer(person: [4], company: [99], city: "Ankara");

        var result = await Parse(Card);

        Assert.Null(result.Contact.DisplayName.Value); // line 4 was the email: not sent, so not accepted
        Assert.Null(result.Contact.CompanyName.Value);
        Assert.Equal("İstanbul", result.Contact.City.Value); // invented city dropped; hint used
    }

    private Task<BusinessCardParseResult> Parse(IEnumerable<string> texts)
    {
        var service = Cards.Service(_llm);
        var request = new BusinessCardParseRequest
        {
            Lines = texts.Select((t, i) => new ParseLineDto
            {
                Text = t,
                Order = i + 1,
                Region = LayoutRegion.Center,
                Size = LayoutSize.Normal,
            }).ToList(),
        };
        return service.ParseAsync(request, CancellationToken.None);
    }
}
