using BusinessCard.Application.BusinessCards.Merging;
using BusinessCard.Application.BusinessCards.Parsing;
using BusinessCard.Domain.Cards;
using static BusinessCard.UnitTests.Support.Cards;

namespace BusinessCard.UnitTests.Merging;

public sealed class LayoutAndContextTests
{
    [Theory]
    [InlineData("Ahmet Yılmaz", true)]
    [InlineData("Dr. Mehmet Ali Öz", true)]
    [InlineData("Anne-Marie O'Neil", true)]
    [InlineData("ACME TECHNOLOGY", false)]
    [InlineData("Enterprise Solutions Manager", false)]
    [InlineData("Büyükdere Cad. No:255", false)]
    [InlineData("NOVA", false)]
    [InlineData("Since 1998", false)]
    public void Recognizes_name_shaped_lines(string text, bool expected)
    {
        Assert.Equal(expected, CardVocabulary.LooksLikePersonName(text));
    }

    [Fact]
    public void Residual_text_strips_extracted_values_and_labels()
    {
        var extraction = DeterministicExtractor.Extract([Line(1, "Ahmet Yılmaz | M: 0532 123 45 67"), Line(2, "ACME A.Ş. - Tel: 0212 555 00 00")]);

        Assert.Equal("Ahmet Yılmaz", extraction.Residuals[1]);
        Assert.Equal("ACME A.Ş.", extraction.Residuals[2]);
    }

    [Fact]
    public void Context_detects_turkish_cards_and_company_domain()
    {
        CardLine[] lines = [Line(1, "ACME TECHNOLOGY"), Line(2, "ahmet.yilmaz@acme.com.tr")];

        var context = CardContext.From(lines, DeterministicExtractor.Extract(lines));

        Assert.True(context.Turkish);
        Assert.Equal("acme", context.CompanyDomainLabel);
        Assert.True(context.EmailMatchesName("Ahmet Yılmaz"));
        Assert.True(context.DomainMatchesCompany("ACME Technology"));
        Assert.False(context.DomainMatchesCompany("NOVA Enerji"));
    }

    [Theory]
    [InlineData("ayilmaz@acme.com")]
    [InlineData("a.yilmaz@acme.com")]
    [InlineData("ahmet@acme.com")]
    public void Email_local_part_variants_match_the_name(string email)
    {
        CardLine[] lines = [Line(1, email)];

        var context = CardContext.From(lines, DeterministicExtractor.Extract(lines));

        Assert.True(context.EmailMatchesName("Ahmet Yılmaz"));
    }

    [Fact]
    public void Webmail_domains_are_not_company_evidence()
    {
        CardLine[] lines = [Line(1, "ahmet.yilmaz@gmail.com")];

        var context = CardContext.From(lines, DeterministicExtractor.Extract(lines));

        Assert.Null(context.CompanyDomainLabel);
    }

    [Theory]
    [InlineData("www.nova-enerji.com.tr", "nova-enerji")]
    [InlineData("mail.acme.co.uk", "acme")]
    [InlineData("acme.io", "acme")]
    public void Domain_label_skips_www_and_public_suffixes(string host, string expected)
    {
        Assert.Equal(expected, CardContext.DomainLabel(host));
    }

    [Fact]
    public void Fallback_reads_a_typical_card()
    {
        var candidates = Classify(
            Line(1, "ACME TECHNOLOGY", LayoutRegion.Top, LayoutSize.Large),
            Line(2, "Ahmet Yılmaz", LayoutRegion.Center, LayoutSize.Large),
            Line(3, "Enterprise Solutions Manager", LayoutRegion.Center, LayoutSize.Normal, sameBlock: true),
            Line(4, "Maslak Mah. Büyükdere Cad. No:255", LayoutRegion.Bottom, LayoutSize.Small),
            Line(5, "34485 Sarıyer/İstanbul", LayoutRegion.Bottom, LayoutSize.Small));

        Assert.Equal(CandidateSource.Layout, candidates.Source);
        Assert.Equal([2], candidates.Person);
        Assert.Equal([3], candidates.Title);
        Assert.Equal([1], candidates.Company);
        Assert.Equal([4, 5], candidates.Address);
    }

    [Fact]
    public void Fallback_NOVA_below_the_person_is_the_company()
    {
        var candidates = Classify(
            Line(1, "Ahmet Yılmaz", LayoutRegion.Top, LayoutSize.Large),
            Line(2, "General Manager", LayoutRegion.Top, LayoutSize.Normal, sameBlock: true),
            Line(3, "NOVA", LayoutRegion.Bottom, LayoutSize.Large));

        Assert.Equal([1], candidates.Person);
        Assert.Equal([2], candidates.Title);
        Assert.Equal([3], candidates.Company);
    }

    [Fact]
    public void Fallback_NOVA_above_the_person_is_the_company()
    {
        var candidates = Classify(
            Line(1, "NOVA", LayoutRegion.Top, LayoutSize.Large),
            Line(2, "Ahmet Yılmaz", LayoutRegion.Center, LayoutSize.Large),
            Line(3, "General Manager", LayoutRegion.Center, LayoutSize.Normal, sameBlock: true));

        Assert.Equal([2], candidates.Person);
        Assert.Equal([3], candidates.Title);
        Assert.Equal([1], candidates.Company);
    }

    [Fact]
    public void Email_match_beats_visual_prominence()
    {
        // A large slogan-like two-word line vs. the small name the email spells out.
        var lines = new[]
        {
            Line(1, "Think Different", LayoutRegion.Center, LayoutSize.Large),
            Line(2, "Zeynep Kaya", LayoutRegion.Bottom, LayoutSize.Small),
        };
        var context = new CardContext(true, ["zeynep", "kaya"], null);

        var candidates = LayoutHeuristics.Classify(lines, context);

        Assert.Equal([2], candidates.Person);
    }

    [Fact]
    public void Company_found_through_the_email_domain()
    {
        var context = new CardContext(true, [], "acme");

        var candidates = LayoutHeuristics.Classify(
            [Line(1, "Ali Veli", LayoutRegion.Center, LayoutSize.Large), Line(2, "Acme Global", LayoutRegion.Bottom, LayoutSize.Small)],
            context);

        Assert.Equal([2], candidates.Company);
    }

    private static SemanticCandidates Classify(params CardLine[] lines) =>
        LayoutHeuristics.Classify(lines, new CardContext(true, [], null));
}
