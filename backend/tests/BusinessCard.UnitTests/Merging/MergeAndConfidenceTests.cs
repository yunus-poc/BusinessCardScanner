using BusinessCard.Application.BusinessCards;
using BusinessCard.Application.BusinessCards.Llm;
using BusinessCard.Domain.Cards;
using BusinessCard.UnitTests.Support;
using static BusinessCard.UnitTests.Support.Cards;

namespace BusinessCard.UnitTests.Merging;

public sealed class MergeAndConfidenceTests
{
    private static readonly BusinessCardParseRequest TurkishCard = Request(
        ("ACME TEKNOLOJİ A.Ş.", LayoutRegion.Top, LayoutSize.Large),               // 1
        ("AHMET YILMAZ", LayoutRegion.Center, LayoutSize.Large),                   // 2
        ("SATIŞ VE PAZARLAMA DİREKTÖRÜ", LayoutRegion.Center, LayoutSize.Normal),  // 3
        ("ahmet.yilmaz@acme.com.tr", LayoutRegion.Bottom, LayoutSize.Small),       // 4 consumed
        ("GSM: 0532 123 45 67", LayoutRegion.Bottom, LayoutSize.Small),            // 5 consumed
        ("Maslak Mah. Büyükdere Cad. No:255", LayoutRegion.Bottom, LayoutSize.Small), // 6
        ("34485 Sarıyer/İSTANBUL", LayoutRegion.Bottom, LayoutSize.Small));        // 7

    private readonly FakeLlmClassifier _llm = new();

    [Fact]
    public async Task Llm_result_is_normalized_and_corroborated_fields_are_confident()
    {
        _llm.Result = FakeLlmClassifier.Answer(person: [2], company: [1], title: [3], address: [6, 7], city: "İSTANBUL");

        var c = (await Service(_llm).ParseAsync(TurkishCard, CancellationToken.None)).Contact;

        Assert.Equal(("Ahmet", "Yılmaz", "Ahmet Yılmaz"), (c.FirstName.Value, c.LastName.Value, c.DisplayName.Value));
        Assert.Equal("ACME Teknoloji A.Ş.", c.CompanyName.Value);
        Assert.Equal("Satış ve Pazarlama Direktörü", c.JobTitle.Value);
        Assert.Equal("Maslak Mah. Büyükdere Cad. No:255, Sarıyer", c.Street.Value);
        Assert.Equal("İstanbul", c.City.Value);
        Assert.Equal("34485", c.PostalCode.Value);
        Assert.Equal("Türkiye", c.Country.Value);

        // Email spells the name; domain matches the company; postal code confirms the city.
        Assert.False(c.FirstName.NeedsReview);
        Assert.True(c.FirstName.Confidence >= 0.95);
        Assert.False(c.CompanyName.NeedsReview);
        Assert.False(c.City.NeedsReview);
        Assert.False(c.JobTitle.NeedsReview);
    }

    [Fact]
    public async Task Without_the_llm_the_layout_fallback_fills_fields_and_flags_them_all()
    {
        _llm.Result = LlmBusinessCardResult.Failed(LlmStatus.Timeout, "phi-4-mini-instruct", TimeSpan.FromSeconds(2.5));

        var result = await Service(_llm).ParseAsync(TurkishCard, CancellationToken.None);

        var c = result.Contact;
        Assert.True(result.Meta.FallbackUsed);
        Assert.Equal("Ahmet Yılmaz", c.DisplayName.Value);
        Assert.Equal("ACME Teknoloji A.Ş.", c.CompanyName.Value);
        Assert.Equal("Satış ve Pazarlama Direktörü", c.JobTitle.Value);
        Assert.All(new[] { c.FirstName, c.LastName, c.CompanyName, c.JobTitle, c.Street }, f => Assert.True(f.NeedsReview));
        // Deterministic fields are unaffected by the model failure.
        Assert.False(c.Email.NeedsReview);
        Assert.False(c.MobilePhone.NeedsReview);
    }

    [Fact]
    public async Task Fallback_never_scores_higher_than_the_llm_path()
    {
        _llm.Result = FakeLlmClassifier.Answer(person: [2], company: [1], title: [3]);
        var withLlm = (await Service(_llm).ParseAsync(TurkishCard, CancellationToken.None)).Contact;
        _llm.Result = LlmBusinessCardResult.Failed(LlmStatus.Unavailable, "m", TimeSpan.Zero);
        var fallback = (await Service(_llm).ParseAsync(TurkishCard, CancellationToken.None)).Contact;

        Assert.True(fallback.FirstName.Confidence < withLlm.FirstName.Confidence);
        Assert.True(fallback.CompanyName.Confidence < withLlm.CompanyName.Confidence);
    }

    [Fact]
    public async Task City_contradicting_the_postal_code_is_flagged()
    {
        // "Ankara" is on the card, but 34485 is an İstanbul postal code.
        var card = Request(
            ("Ali Veli", LayoutRegion.Center, LayoutSize.Large),
            ("Ankara Cad. No:5 34485 Sarıyer/İstanbul", LayoutRegion.Bottom, LayoutSize.Small));
        _llm.Result = FakeLlmClassifier.Answer(person: [1], address: [2], city: "Ankara");

        var c = (await Service(_llm).ParseAsync(card, CancellationToken.None)).Contact;

        Assert.Equal("Ankara", c.City.Value);
        Assert.True(c.City.NeedsReview);
    }

    [Fact]
    public async Task One_word_name_and_grounding_corrections_lower_confidence()
    {
        var card = Request(("Ahmet", LayoutRegion.Center, LayoutSize.Normal), ("NOVA", LayoutRegion.Top, LayoutSize.Large));
        _llm.Result = FakeLlmClassifier.Answer(person: [1], company: [2]);
        var clean = (await Service(_llm).ParseAsync(card, CancellationToken.None)).Contact;
        // Same answer, plus a claim about a line that was never sent (id 42).
        _llm.Result = FakeLlmClassifier.Answer(person: [1], company: [2, 42]);
        var corrected = (await Service(_llm).ParseAsync(card, CancellationToken.None)).Contact;

        Assert.Equal("Ahmet", clean.FirstName.Value);
        Assert.Null(clean.LastName.Value);
        Assert.True(clean.FirstName.NeedsReview);
        Assert.Equal("NOVA", corrected.CompanyName.Value);
        Assert.True(corrected.CompanyName.Confidence < clean.CompanyName.Confidence);
    }

    [Fact]
    public async Task Validated_values_never_end_up_inside_semantic_fields()
    {
        var card = Request(
            ("Ahmet Yılmaz | M: 0532 123 45 67", LayoutRegion.Center, LayoutSize.Large),
            ("ACME A.Ş. - www.acme.com.tr", LayoutRegion.Top, LayoutSize.Large));
        _llm.Result = FakeLlmClassifier.Answer(person: [1], company: [2]);

        var result = await Service(_llm).ParseAsync(card, CancellationToken.None);

        Assert.Equal("Ahmet Yılmaz", result.Contact.DisplayName.Value);
        Assert.Equal("ACME A.Ş.", result.Contact.CompanyName.Value);
        Assert.Equal("+90 532 123 45 67", result.Contact.MobilePhone.Value);
        Assert.Equal("https://www.acme.com.tr", result.Contact.Website.Value);
        // And the model only ever saw the residual text.
        Assert.DoesNotContain(_llm.Requests.Single().Lines, l => l.Text.Contains("0532", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Review_threshold_is_configurable()
    {
        _llm.Result = FakeLlmClassifier.Answer(person: [2], company: [1], title: [3]);

        var strict = (await Service(_llm, reviewThreshold: 0.99).ParseAsync(TurkishCard, CancellationToken.None)).Contact;
        var lenient = (await Service(_llm, reviewThreshold: 0.5).ParseAsync(TurkishCard, CancellationToken.None)).Contact;

        Assert.True(strict.JobTitle.NeedsReview);
        Assert.False(lenient.JobTitle.NeedsReview);
    }

    [Fact]
    public async Task Confidence_values_are_bounded_and_rounded()
    {
        _llm.Result = FakeLlmClassifier.Answer(person: [2], company: [1], title: [3], address: [6, 7], city: "İstanbul");

        var c = (await Service(_llm).ParseAsync(TurkishCard, CancellationToken.None)).Contact;

        foreach (var field in new[] { c.FirstName, c.CompanyName, c.JobTitle, c.Street, c.City, c.Email, c.MobilePhone })
        {
            Assert.InRange(field.Confidence, 0, 0.99);
            Assert.Equal(Math.Round(field.Confidence, 2), field.Confidence);
        }
    }
}
