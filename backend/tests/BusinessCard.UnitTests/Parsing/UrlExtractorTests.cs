using BusinessCard.Application.BusinessCards.Parsing;

namespace BusinessCard.UnitTests.Parsing;

public sealed class UrlExtractorTests
{
    [Theory]
    [InlineData("www.acme.com.tr", "https://www.acme.com.tr")]
    [InlineData("WWW.ACME.COM.TR", "https://www.acme.com.tr")]
    [InlineData("Web: acme.com.tr", "https://acme.com.tr")]
    [InlineData("https://Acme.com/Contact/", "https://acme.com/Contact")]
    [InlineData("http://acme.io", "http://acme.io")]
    [InlineData("www.nova-enerji.com.tr.", "https://www.nova-enerji.com.tr")]
    public void Extracts_and_normalizes_websites(string line, string expected)
    {
        var (urls, _) = UrlExtractor.Extract(line);

        var url = Assert.Single(urls);
        Assert.Equal(expected, url.Value);
        Assert.False(url.IsSocial);
    }

    [Theory]
    [InlineData("linkedin.com/in/ahmetyilmaz")]
    [InlineData("www.linkedin.com/company/acme")]
    [InlineData("instagram.com/acme")]
    public void Recognizes_social_profiles(string line)
    {
        var (urls, _) = UrlExtractor.Extract(line);

        Assert.True(Assert.Single(urls).IsSocial);
    }

    [Theory]
    [InlineData("ACME Teknoloji A.Ş.")]
    [InlineData("Ltd. Şti.")]
    [InlineData("Büyükdere Cad. No.5")]
    [InlineData("Ahmet Yılmaz")]
    [InlineData("http://user:secret@evil.com")]
    public void Ignores_text_that_only_looks_like_a_domain(string line)
    {
        var (urls, _) = UrlExtractor.Extract(line);

        Assert.Empty(urls);
    }
}
