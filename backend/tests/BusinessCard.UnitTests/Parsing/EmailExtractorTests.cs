using BusinessCard.Application.BusinessCards.Parsing;

namespace BusinessCard.UnitTests.Parsing;

public sealed class EmailExtractorTests
{
    [Theory]
    [InlineData("ahmet.yilmaz@acme.com.tr", "ahmet.yilmaz@acme.com.tr")]
    [InlineData("E-mail: Ahmet.Yilmaz@ACME.com.tr", "ahmet.yilmaz@acme.com.tr")]
    [InlineData("e-posta: info@nova-enerji.com", "info@nova-enerji.com")]
    [InlineData("<sales+tr@acme.io>.", "sales+tr@acme.io")]
    public void Extracts_and_lowercases_valid_addresses(string line, string expected)
    {
        var (emails, _) = EmailExtractor.Extract(line);

        var email = Assert.Single(emails);
        Assert.Equal(expected, email.Value);
        Assert.False(email.Repaired);
    }

    [Theory]
    [InlineData("ahmet.yilmaz @ acme.com", "ahmet.yilmaz@acme.com")]
    [InlineData("ahmet@acme,com", "ahmet@acme.com")]
    [InlineData("ahmet@acme . com.tr", "ahmet@acme.com.tr")]
    [InlineData("ahmet.yılmaz@acme.com", "ahmet.yilmaz@acme.com")]
    [InlineData("ŞEBNEM.ÖZ@ACME.COM", "sebnem.oz@acme.com")]
    public void Repairs_common_ocr_damage_and_flags_it(string line, string expected)
    {
        var (emails, _) = EmailExtractor.Extract(line);

        var email = Assert.Single(emails);
        Assert.Equal(expected, email.Value);
        Assert.True(email.Repaired);
    }

    [Fact]
    public void Comma_after_an_address_is_not_read_as_a_dot()
    {
        var (emails, _) = EmailExtractor.Extract("ahmet@acme.com, Tel: 0212 555 00 00");

        Assert.Equal("ahmet@acme.com", Assert.Single(emails).Value);
    }

    [Fact]
    public void Finds_several_addresses_and_removes_them_from_the_line()
    {
        var (emails, remaining) = EmailExtractor.Extract("ali@acme.com / satis@acme.com");

        Assert.Equal(["ali@acme.com", "satis@acme.com"], emails.Select(e => e.Value));
        Assert.DoesNotContain("@", remaining, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ahmet@acme")]
    [InlineData("@acme.com")]
    [InlineData("ahmet@@acme.com")]
    [InlineData("ahmet@-acme.com")]
    [InlineData("Ahmet Yılmaz")]
    public void Ignores_invalid_or_absent_addresses(string line)
    {
        var (emails, _) = EmailExtractor.Extract(line);

        Assert.Empty(emails);
    }
}
