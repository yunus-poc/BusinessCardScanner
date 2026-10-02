using BusinessCard.Application.BusinessCards;
using BusinessCard.Application.BusinessCards.Parsing;
using BusinessCard.Domain.Cards;
using BusinessCard.UnitTests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace BusinessCard.UnitTests.Parsing;

public sealed class DeterministicParsingTests
{
    private static readonly string[] SampleCard =
    [
        "ACME TECHNOLOGY",
        "Ahmet Yılmaz",
        "Enterprise Solutions Manager",
        "E: ahmet.yilmaz@acme.com.tr",
        "M: +90 532 123 45 67 | T: +90 212 555 00 00",
        "F: +90 212 555 00 01",
        "www.acme.com.tr",
        "Maslak Mah. Büyükdere Cad. No:255",
        "34485 Sarıyer/İstanbul",
    ];

    [Fact]
    public void Extracts_all_deterministic_values_from_a_typical_card()
    {
        var extraction = DeterministicExtractor.Extract(Lines(SampleCard));

        Assert.Equal("ahmet.yilmaz@acme.com.tr", Assert.Single(extraction.Emails).Value);
        Assert.Equal(
            [(PhoneKind.Mobile, "+905321234567"), (PhoneKind.Business, "+902125550000"), (PhoneKind.Fax, "+902125550001")],
            extraction.Phones.Select(p => (p.Kind, p.E164)));
        Assert.Equal("https://www.acme.com.tr", Assert.Single(extraction.Urls).Value);
        Assert.Equal("34485", extraction.PostalCode?.Value);
        Assert.Equal("İstanbul", extraction.PostalCode?.CityHint);
    }

    [Fact]
    public void Marks_only_pure_contact_lines_as_consumed()
    {
        var extraction = DeterministicExtractor.Extract(Lines(SampleCard));

        // Email, phones, fax, website lines; names, title and address stay for the LLM.
        Assert.Equal([4, 5, 6, 7], extraction.ConsumedLineOrders.Order());
    }

    [Fact]
    public void A_line_with_a_name_next_to_the_number_is_not_consumed()
    {
        var extraction = DeterministicExtractor.Extract(Lines(["Ahmet Yılmaz 0532 123 45 67"]));

        Assert.Single(extraction.Phones);
        Assert.Empty(extraction.ConsumedLineOrders);
    }

    [Fact]
    public void Email_domain_is_never_taken_as_the_website()
    {
        var extraction = DeterministicExtractor.Extract(Lines(["info@acme.com.tr"]));

        Assert.Single(extraction.Emails);
        Assert.Empty(extraction.Urls);
    }

    [Fact]
    public void Phone_digits_are_never_taken_as_a_postal_code()
    {
        var extraction = DeterministicExtractor.Extract(Lines(["Cad. No:5 Tel: 0212 345 67 89"]));

        Assert.Null(extraction.PostalCode);
    }

    [Fact]
    public void The_same_value_on_two_lines_is_reported_once()
    {
        var extraction = DeterministicExtractor.Extract(Lines(["0532 123 45 67", "GSM +90 532 123 45 67"]));

        Assert.Single(extraction.Phones);
    }

    [Fact]
    public async Task Service_maps_extraction_into_contact_fields()
    {
        var service = Cards.Service();
        string[] card = [.. SampleCard, "satis@acme.com.tr", "linkedin.com/in/ahmetyilmaz", "+90 216 444 00 00"];

        var result = await service.ParseAsync(Request(card), CancellationToken.None);

        var contact = result.Contact;
        Assert.Equal(new ParsedField("ahmet.yilmaz@acme.com.tr", 0.97, false), contact.Email);
        Assert.Equal("+90 532 123 45 67", contact.MobilePhone.Value);
        Assert.Equal("+90 212 555 00 00", contact.BusinessPhone.Value);
        Assert.False(contact.BusinessPhone.NeedsReview);
        Assert.Equal("https://www.acme.com.tr", contact.Website.Value);
        Assert.Equal("34485", contact.PostalCode.Value);
        Assert.Equal("İstanbul", contact.City.Value);
        Assert.Equal("Türkiye", contact.Country.Value);
        Assert.Equal("https://linkedin.com/in/ahmetyilmaz", contact.Notes.Value);

        Assert.Equal(["satis@acme.com.tr"], result.AdditionalEmails);
        // The fax is dropped; the second landline is offered.
        Assert.Equal(["+90 216 444 00 00"], result.AdditionalPhones);
        Assert.False(result.Meta.LlmUsed);

        // Without a model, layout heuristics fill semantic fields, always flagged for review.
        Assert.True(result.Meta.FallbackUsed);
        Assert.Equal("Ahmet", contact.FirstName.Value);
        Assert.True(contact.FirstName.NeedsReview);
    }

    [Fact]
    public async Task Repaired_email_and_untyped_phone_are_flagged_for_review()
    {
        var service = Cards.Service();

        var result = await service.ParseAsync(Request(["ahmet @ acme.com", "+1 201-555-0123"]), CancellationToken.None);

        Assert.True(result.Contact.Email.NeedsReview);
        Assert.True(result.Contact.Email.Confidence < 0.8);
        Assert.Equal("+1 201-555-0123", result.Contact.BusinessPhone.Value);
        Assert.True(result.Contact.BusinessPhone.NeedsReview);
    }

    private static List<CardLine> Lines(IEnumerable<string> texts) =>
        texts.Select((t, i) => new CardLine(t, i + 1, LayoutRegion.Center, LayoutSize.Normal, LayoutAlign.Left, false, LayoutGap.None))
            .ToList();

    private static BusinessCardParseRequest Request(IEnumerable<string> texts) => new()
    {
        Lines = texts.Select((t, i) => new ParseLineDto
        {
            Text = t,
            Order = i + 1,
            Region = LayoutRegion.Center,
            Size = LayoutSize.Normal,
        }).ToList(),
    };
}
