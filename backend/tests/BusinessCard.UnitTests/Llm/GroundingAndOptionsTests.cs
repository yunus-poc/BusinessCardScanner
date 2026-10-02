using BusinessCard.Application.BusinessCards.Llm;
using BusinessCard.Domain.Cards;
using BusinessCard.Infrastructure.Llm;

namespace BusinessCard.UnitTests.Llm;

public sealed class GroundingAndOptionsTests
{
    private static readonly CardLine[] Sent =
    [
        Line(1, "NOVA"),
        Line(2, "Zeynep Kaya"),
        Line(3, "Satış Direktörü"),
        Line(8, "Maslak Mah. Büyükdere Cad. No:255"),
        Line(9, "34485 Sarıyer/İSTANBUL"),
    ];

    [Fact]
    public void Keeps_a_fully_supported_classification_unchanged()
    {
        var raw = Classification(person: [2], company: [1], title: [3], address: [8, 9], city: "İstanbul");

        var grounded = LlmClassificationGrounding.Ground(raw, Sent);

        Assert.Equal(0, grounded.Corrections);
        Assert.Equal([2], grounded.Classification.Person);
        Assert.Equal([8, 9], grounded.Classification.Address);
        Assert.Equal("İstanbul", grounded.Classification.City);
    }

    [Fact]
    public void Drops_line_ids_that_were_never_sent()
    {
        // Line 5 was an email line consumed deterministically; 42 never existed.
        var grounded = LlmClassificationGrounding.Ground(Classification(person: [2], company: [5, 42]), Sent);

        Assert.Empty(grounded.Classification.Company);
        Assert.Equal(2, grounded.Corrections);
    }

    [Fact]
    public void A_line_claimed_twice_goes_to_the_higher_priority_field()
    {
        var grounded = LlmClassificationGrounding.Ground(Classification(person: [2], company: [1], title: [2, 3]), Sent);

        Assert.Equal([2], grounded.Classification.Person);
        Assert.Equal([3], grounded.Classification.Title);
        Assert.Equal(1, grounded.Corrections);
    }

    [Theory]
    [InlineData("Istanbul")]
    [InlineData("istanbul")]
    [InlineData(" İstanbul ")]
    public void City_written_on_the_card_is_accepted_regardless_of_case_and_diacritics(string city)
    {
        var grounded = LlmClassificationGrounding.Ground(Classification(city: city), Sent);

        Assert.Equal(city.Trim(), grounded.Classification.City);
    }

    [Theory]
    [InlineData("Ankara")]
    [InlineData("Türkiye")]
    [InlineData("Ist")]
    public void Places_not_written_on_the_card_are_removed(string invented)
    {
        var grounded = LlmClassificationGrounding.Ground(Classification(city: invented, country: invented), Sent);

        Assert.Null(grounded.Classification.City);
        Assert.Null(grounded.Classification.Country);
        Assert.Equal(2, grounded.Corrections);
    }

    [Fact]
    public void Disabled_provider_needs_no_further_configuration()
    {
        var result = new AiOptionsValidator().Validate(null, new AiOptions { Provider = AiProvider.Disabled });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Enabled_provider_requires_a_valid_url_timeout_and_token_cap()
    {
        var result = new AiOptionsValidator().Validate(null, new AiOptions
        {
            BaseUrl = null,
            TimeoutMilliseconds = 50,
            MaxOutputTokens = 5_000,
        });

        Assert.True(result.Failed);
        Assert.Equal(3, result.Failures!.Count());
    }

    [Fact]
    public void Default_enabled_configuration_with_url_is_valid()
    {
        var result = new AiOptionsValidator().Validate(null, new AiOptions { BaseUrl = new Uri("http://10.0.4.12:8080") });

        Assert.True(result.Succeeded);
    }

    private static CardLine Line(int order, string text) =>
        new(text, order, LayoutRegion.Center, LayoutSize.Normal, LayoutAlign.Left, false, LayoutGap.None);

    private static LlmClassification Classification(
        int[]? person = null,
        int[]? company = null,
        int[]? title = null,
        int[]? address = null,
        string? city = null,
        string? country = null) =>
        new(person ?? [], company ?? [], title ?? [], [], address ?? [], city, null, country);
}
