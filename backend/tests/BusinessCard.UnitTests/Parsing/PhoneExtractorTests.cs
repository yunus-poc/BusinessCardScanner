using BusinessCard.Application.BusinessCards.Parsing;

namespace BusinessCard.UnitTests.Parsing;

public sealed class PhoneExtractorTests
{
    [Theory]
    [InlineData("0532 123 45 67")]
    [InlineData("532 123 45 67")]
    [InlineData("+90 532 123 45 67")]
    [InlineData("+90 (532) 123-45-67")]
    [InlineData("0090 532 123 45 67")]
    [InlineData("(0532) 1234567")]
    [InlineData("+905321234567")]
    public void Normalizes_turkish_mobile_numbers(string line)
    {
        var (phones, _) = PhoneExtractor.Extract(line);

        var phone = Assert.Single(phones);
        Assert.Equal("+905321234567", phone.E164);
        Assert.Equal("+90 532 123 45 67", phone.Display);
        Assert.Equal(PhoneKind.Mobile, phone.Kind);
    }

    [Theory]
    [InlineData("0212 555 00 00")]
    [InlineData("+90 (212) 555 00 00")]
    [InlineData("Tel: (0212) 555-0000")]
    public void Normalizes_turkish_landlines_as_business(string line)
    {
        var (phones, _) = PhoneExtractor.Extract(line);

        var phone = Assert.Single(phones);
        Assert.Equal("+902125550000", phone.E164);
        Assert.Equal(PhoneKind.Business, phone.Kind);
    }

    [Theory]
    [InlineData("+44 20 7946 0958", "+442079460958")]
    [InlineData("+49 30 12345678", "+493012345678")]
    [InlineData("Tel +1 201-555-0123", "+12015550123")]
    public void Normalizes_international_numbers(string line, string e164)
    {
        var (phones, _) = PhoneExtractor.Extract(line);

        Assert.Equal(e164, Assert.Single(phones).E164);
    }

    [Fact]
    public void Number_type_wins_over_a_generic_label()
    {
        // People often print their mobile after "Tel:".
        var (phones, _) = PhoneExtractor.Extract("Tel: 0532 123 45 67");

        Assert.Equal(PhoneKind.Mobile, Assert.Single(phones).Kind);
    }

    [Theory]
    [InlineData("Fax: +90 212 555 00 01")]
    [InlineData("F +90 212 555 00 01")]
    [InlineData("Faks: (0212) 555 00 01")]
    public void Fax_label_marks_the_number_as_fax(string line)
    {
        var (phones, _) = PhoneExtractor.Extract(line);

        Assert.Equal(PhoneKind.Fax, Assert.Single(phones).Kind);
    }

    [Fact]
    public void Labels_decide_when_the_number_plan_is_ambiguous()
    {
        var (labelled, _) = PhoneExtractor.Extract("Mobile: +1 201-555-0123");
        var (unlabelled, _) = PhoneExtractor.Extract("+1 201-555-0123");

        Assert.Equal(PhoneKind.Mobile, Assert.Single(labelled).Kind);
        Assert.Equal(PhoneKind.Unknown, Assert.Single(unlabelled).Kind);
    }

    [Fact]
    public void Reads_each_label_for_several_numbers_on_one_line()
    {
        var (phones, remaining) = PhoneExtractor.Extract("T: +90 212 555 00 00  F: +90 212 555 00 01  GSM: 0532 123 45 67");

        Assert.Equal(
            [PhoneKind.Business, PhoneKind.Fax, PhoneKind.Mobile],
            phones.Select(p => p.Kind));
        Assert.DoesNotContain("555", remaining, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("34485 Sarıyer / İstanbul")]
    [InlineData("Büyükdere Cad. No: 255 Kat: 12")]
    [InlineData("Since 2024")]
    [InlineData("Ahmet Yılmaz")]
    [InlineData("0532 123")]
    public void Ignores_digits_that_are_not_valid_phone_numbers(string line)
    {
        var (phones, _) = PhoneExtractor.Extract(line);

        Assert.Empty(phones);
    }
}
