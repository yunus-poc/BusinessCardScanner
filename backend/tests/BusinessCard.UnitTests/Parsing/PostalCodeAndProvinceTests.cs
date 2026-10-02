using BusinessCard.Application.BusinessCards.Parsing;

namespace BusinessCard.UnitTests.Parsing;

public sealed class PostalCodeAndProvinceTests
{
    [Theory]
    [InlineData("Maslak Mah. Büyükdere Cad. No:255 34485 Sarıyer/İstanbul", "34485", "İstanbul")]
    [InlineData("06510 Çankaya / ANKARA", "06510", "Ankara")]
    [InlineData("35210 Konak - Izmir", "35210", "İzmir")]
    public void Extracts_postal_code_corroborated_by_the_named_province(string line, string code, string city)
    {
        var postal = PostalCodeExtractor.Extract(line);

        Assert.NotNull(postal);
        Assert.Equal(code, postal.Value);
        Assert.Equal(city, postal.CityHint);
        Assert.True(postal.Corroborated);
    }

    [Fact]
    public void Derives_the_city_from_the_prefix_when_only_an_address_keyword_is_present()
    {
        var postal = PostalCodeExtractor.Extract("Plaza Kat 3 34394");

        Assert.NotNull(postal);
        Assert.Equal("İstanbul", postal.CityHint);
        Assert.False(postal.Corroborated);
    }

    [Fact]
    public void A_province_contradicting_the_prefix_is_kept_but_not_corroborated()
    {
        var postal = PostalCodeExtractor.Extract("34485 Ankara");

        Assert.NotNull(postal);
        Assert.Equal("Ankara", postal.CityHint);
        Assert.False(postal.Corroborated);
    }

    [Theory]
    [InlineData("34485")]
    [InlineData("Order 99123 units")]
    [InlineData("Ref 12345 abc")]
    [InlineData("Ahmet Yılmaz")]
    public void Ignores_anything_that_is_not_obviously_an_address(string line)
    {
        Assert.Null(PostalCodeExtractor.Extract(line));
    }

    [Fact]
    public void Province_table_is_complete_and_in_plate_order()
    {
        Assert.Equal(81, TurkishProvinces.Count);
        Assert.Equal("Adana", TurkishProvinces.ForPlateCode(1));
        Assert.Equal("Ankara", TurkishProvinces.ForPlateCode(6));
        Assert.Equal("İstanbul", TurkishProvinces.ForPlateCode(34));
        Assert.Equal("İzmir", TurkishProvinces.ForPlateCode(35));
        Assert.Equal("Düzce", TurkishProvinces.ForPlateCode(81));
        Assert.Null(TurkishProvinces.ForPlateCode(0));
        Assert.Null(TurkishProvinces.ForPlateCode(82));
    }

    [Theory]
    [InlineData("Sarıyer/ISTANBUL", "İstanbul")]
    [InlineData("Şehitkamil, Antep", "Gaziantep")]
    [InlineData("IĞDIR", "Iğdır")]
    [InlineData("Kadıköy İstanbul / Türkiye", "İstanbul")]
    public void Finds_provinces_regardless_of_case_and_diacritics(string text, string expected)
    {
        Assert.Equal(expected, TurkishProvinces.FindIn(text));
    }

    [Fact]
    public void Province_names_must_be_whole_words()
    {
        Assert.Null(TurkishProvinces.FindIn("Vanguard Bolumu"));
    }
}
