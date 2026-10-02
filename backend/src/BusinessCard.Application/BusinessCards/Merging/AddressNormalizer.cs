using System.Text.RegularExpressions;
using BusinessCard.Application.BusinessCards.Parsing;

namespace BusinessCard.Application.BusinessCards.Merging;

/// <summary>
/// Builds the street field from address lines and canonicalizes city/country names.
/// Postal code and city have their own fields, so they are removed from the street;
/// the district (e.g. "Sarıyer") stays, because Outlook has no district field.
/// </summary>
internal static partial class AddressNormalizer
{
    private static readonly HashSet<string> TurkeyNames = ["turkiye", "turkey", "tr", "turkei", "turquie", "republic of turkiye"];

    public static string? Street(IEnumerable<string> addressLines, string? postalCode, string? city)
    {
        var parts = new List<string>();
        foreach (var line in addressLines)
        {
            var text = line;
            if (postalCode is not null)
            {
                text = text.Replace(postalCode, " ", StringComparison.Ordinal);
            }
            if (city is not null)
            {
                text = RemoveWord(text, city);
            }
            text = DanglingSeparators().Replace(text, " ");
            text = Whitespace().Replace(text, " ").Trim(' ', ',', '/', '-', '–');
            if (text.Count(char.IsLetterOrDigit) >= 2)
            {
                parts.Add(text);
            }
        }
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>Known provinces get their canonical spelling ("ISTANBUL" → "İstanbul").</summary>
    public static string City(string city) =>
        TurkishProvinces.FindIn(city) is { } province && TextFold.Fold(province) == TextFold.Fold(city.Trim())
            ? province
            : TextCasing.Place(city.Trim());

    public static string Country(string country) =>
        TurkeyNames.Contains(TextFold.Fold(country.Trim().TrimEnd('.')))
            ? TurkishProvinces.Country
            : TextCasing.Place(country.Trim());

    /// <summary>Removes a whole word, diacritic- and case-insensitively ("/ISTANBUL" for city "İstanbul").</summary>
    private static string RemoveWord(string text, string word)
    {
        var folded = TextFold.Fold(text);
        var target = TextFold.Fold(word);
        // Folding preserves length for NFC Turkish/Latin text, so indexes map back 1:1.
        if (folded.Length != text.Length)
        {
            return text;
        }
        var match = Regex.Match(folded, $@"(?<![\p{{L}}]){Regex.Escape(target)}(?![\p{{L}}])");
        return match.Success ? text.Remove(match.Index, match.Length) : text;
    }

    [GeneratedRegex(@"\s*/\s*(?=$|,)|^\s*/\s*|\s+/\s+(?=\s|$)")]
    private static partial Regex DanglingSeparators();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
