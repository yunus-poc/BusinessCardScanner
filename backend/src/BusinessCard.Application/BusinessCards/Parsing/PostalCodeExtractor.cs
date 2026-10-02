using System.Globalization;
using System.Text.RegularExpressions;

namespace BusinessCard.Application.BusinessCards.Parsing;

/// <param name="CityHint">Province named in the line, else derived from the postal prefix.</param>
/// <param name="Corroborated">The province named in the line matches the postal prefix.</param>
internal sealed record PostalMatch(string Value, string? CityHint, bool Corroborated);

/// <summary>
/// Only obvious cases: a Turkish 5-digit postal code (plate prefix 01–81) inside an
/// address-like line. Run after phone extraction, so phone digits are already gone.
/// Anything less clear is left to the LLM.
/// </summary>
internal static partial class PostalCodeExtractor
{
    public static PostalMatch? Extract(string text)
    {
        if (text.Count(char.IsLetter) < 3)
        {
            return null; // a bare number is not obviously an address
        }

        foreach (Match match in FiveDigits().Matches(text))
        {
            var plateCode = int.Parse(match.Value[..2], CultureInfo.InvariantCulture);
            var fromPrefix = TurkishProvinces.ForPlateCode(plateCode);
            if (fromPrefix is null)
            {
                continue;
            }
            var named = TurkishProvinces.FindIn(text);
            if (named is null && !AddressKeyword().IsMatch(TextFold.Fold(text)))
            {
                continue; // five digits next to random words: not obvious
            }
            return new PostalMatch(match.Value, named ?? fromPrefix, named == fromPrefix);
        }
        return null;
    }

    [GeneratedRegex(@"(?<!\d)\d{5}(?!\d)")]
    private static partial Regex FiveDigits();

    // mahalle, cadde, sokak, bulvar, no, kat, daire, plaza, osb, posta kodu…
    [GeneratedRegex(@"(?<![a-z])(mah|mahallesi|cad|caddesi|cd|sok|sokak|sk|bulvar|bulvari|blv|no|kat|daire|plaza|osb|pk|posta)(?![a-z])")]
    private static partial Regex AddressKeyword();
}
