using BusinessCard.Application.BusinessCards;
using BusinessCard.Application.BusinessCards.Parsing;
using BusinessCard.Domain.Cards;

namespace BusinessCard.UnitTests.Parsing;

public sealed class SanitizerTests
{
    [Theory]
    [InlineData("Ah​met\u0007 Yılmaz", "Ahmet Yılmaz")]
    [InlineData("‮evil‬ name", "evil name")]
    [InlineData("ACME\tTECH\r\nLTD", "ACME TECH LTD")]
    [InlineData("   spaced    out   ", "spaced out")]
    [InlineData("﻿NOVA", "NOVA")]
    public void Removes_control_and_invisible_characters(string input, string expected)
    {
        Assert.Equal(expected, CardLineSanitizer.Clean(input));
    }

    [Fact]
    public void Normalizes_decomposed_turkish_letters()
    {
        // "I" + combining dot above, as some OCR engines emit it.
        Assert.Equal("İstanbul", CardLineSanitizer.Clean("İstanbul"));
    }

    [Fact]
    public void Orders_lines_and_drops_noise_and_empty_lines()
    {
        var lines = CardLineSanitizer.Sanitize(
        [
            Line(3, "ahmet@acme.com"),
            Line(1, "|||"),
            Line(2, "Ahmet Yılmaz"),
            Line(4, "​"),
        ]);

        Assert.Equal(["Ahmet Yılmaz", "ahmet@acme.com"], lines.Select(l => l.Text));
        Assert.Equal([2, 3], lines.Select(l => l.Order));
    }

    [Theory]
    [InlineData("|||", true)]
    [InlineData("•", true)]
    [InlineData("Ü", true)]
    [InlineData("IIIIII", true)]
    [InlineData("® ™ ©", true)]
    [InlineData("=/=/=/=a", true)]
    [InlineData("A.Ş.", false)]
    [InlineData("NOVA", false)]
    [InlineData("+90 212 555 00 00", false)]
    [InlineData("Ltd. Şti.", false)]
    [InlineData("İK", false)]
    public void Noise_filter_is_conservative(string text, bool isNoise)
    {
        Assert.Equal(isNoise, OcrNoiseFilter.IsNoise(text));
    }

    [Theory]
    [InlineData("İSTANBUL", "istanbul")]
    [InlineData("MOBILE", "mobile")]
    [InlineData("IĞDIR", "igdir")]
    [InlineData("Çağrı Şenöz", "cagri senoz")]
    public void Fold_matches_turkish_and_english_case_alike(string input, string expected)
    {
        Assert.Equal(expected, TextFold.Fold(input));
    }

    private static ParseLineDto Line(int order, string text) => new()
    {
        Text = text,
        Order = order,
        Region = LayoutRegion.Center,
        Size = LayoutSize.Normal,
    };
}
