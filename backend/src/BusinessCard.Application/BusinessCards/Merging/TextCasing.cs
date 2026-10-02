using System.Buffers;
using System.Globalization;
using BusinessCard.Application.BusinessCards.Parsing;

namespace BusinessCard.Application.BusinessCards.Merging;

/// <summary>
/// Readable casing for text printed in capitals. Only ALL-CAPS or all-lowercase text
/// is changed; anything with deliberate mixed case ("McDonald", "eBay", "iOS") is left
/// alone.
/// </summary>
/// <remarks>
/// Turkish lower-casing maps "I" to dotless "ı". That is right for "YILMAZ" → "Yılmaz"
/// but wrong for English words ("ENTERPRISE" → "Enterprıse"), and Turkish cards often
/// carry English titles and company names. So person names follow the card's language,
/// while company names and titles use Turkish rules only when that text itself contains
/// a Turkish letter ("ENERJİ YATIRIMLARI" → "Enerji Yatırımları").
/// </remarks>
internal static class TextCasing
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly HashSet<string> Connectors = ["ve", "ile", "and", "of", "for", "the", "de", "da", "und", "et", "y"];

    /// <summary>Folded legal forms → canonical spelling.</summary>
    private static readonly Dictionary<string, string> LegalForms = new()
    {
        ["a.s."] = "A.Ş.", ["as"] = "A.Ş.", ["ltd."] = "Ltd.", ["ltd"] = "Ltd.", ["sti."] = "Şti.", ["sti"] = "Şti.",
        ["san."] = "San.", ["tic."] = "Tic.", ["inc."] = "Inc.", ["inc"] = "Inc.", ["llc"] = "LLC", ["gmbh"] = "GmbH",
        ["corp."] = "Corp.", ["co."] = "Co.", ["plc"] = "PLC", ["ag"] = "AG", ["bv"] = "BV",
    };

    public static bool NeedsRecasing(string text)
    {
        var letters = text.Where(char.IsLetter).ToList();
        return letters.Count > 0 && (letters.All(char.IsUpper) || letters.All(char.IsLower));
    }

    /// <summary>"AHMET YILMAZ" → "Ahmet Yılmaz"; "ayşe-nur" → "Ayşe-Nur"; "O'NEIL" → "O'Neil".</summary>
    public static string PersonName(string text, bool turkish) =>
        NeedsRecasing(text) ? MapWords(text, w => TitleWord(w, Culture(turkish))) : text;

    /// <summary>
    /// "ACME TECHNOLOGY" → "ACME Technology". Short all-caps words (≤ 4 letters) are kept as
    /// probable brands/acronyms; legal forms get their canonical spelling.
    /// </summary>
    public static string Company(string text)
    {
        if (!NeedsRecasing(text))
        {
            return text;
        }
        var culture = Culture(HasTurkishLetters(text));
        var allLower = text.Where(char.IsLetter).All(char.IsLower);
        return MapWords(text, word =>
        {
            if (LegalForms.TryGetValue(TextFold.Fold(word), out var legal))
            {
                return legal;
            }
            if (Connectors.Contains(TextFold.Fold(word)))
            {
                return word.ToLower(culture);
            }
            return !allLower && word.Count(char.IsLetter) <= 4 ? word : TitleWord(word, culture);
        });
    }

    /// <summary>"SATIŞ VE PAZARLAMA DİREKTÖRÜ" → "Satış ve Pazarlama Direktörü"; "CEO" stays.</summary>
    public static string Title(string text)
    {
        if (!NeedsRecasing(text))
        {
            return text;
        }
        var culture = Culture(HasTurkishLetters(text));
        var allLower = text.Where(char.IsLetter).All(char.IsLower);
        var first = true;
        return MapWords(text, word =>
        {
            var isFirst = first;
            first = false;
            if (!isFirst && Connectors.Contains(TextFold.Fold(word)))
            {
                return word.ToLower(culture);
            }
            // Acronyms such as CEO, CTO, VP, IT, İK stay in capitals.
            return !allLower && word.Count(char.IsLetter) <= 3 ? word : TitleWord(word, culture);
        });
    }

    /// <summary>
    /// "LONDON" → "London", "ÜSKÜDAR" → "Üsküdar". Field-based like companies, so "PARIS"
    /// never becomes "Parıs"; known Turkish provinces are canonicalized by callers first.
    /// </summary>
    public static string Place(string text) => PersonName(text, HasTurkishLetters(text));

    private static readonly SearchValues<char> TurkishLetters = SearchValues.Create("çğıİöşüÇĞÖŞÜ");

    public static bool HasTurkishLetters(string text) => text.AsSpan().ContainsAny(TurkishLetters);

    private static CultureInfo Culture(bool turkish) => turkish ? Turkish : CultureInfo.InvariantCulture;

    private static string MapWords(string text, Func<string, string> map) =>
        string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(map));

    private static string TitleWord(string word, CultureInfo culture)
    {
        // Hyphenated parts are each capitalized; after an apostrophe only for one-letter
        // prefixes (O'Neil, D'Angelo), not Turkish suffixes (İstanbul'da).
        var parts = word.Split('-');
        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = CapitalizeFirst(parts[i].ToLower(culture), culture);
            var apostrophe = parts[i].IndexOfAny(['\'', '’']);
            if (apostrophe == 1 && parts[i].Length > 2)
            {
                parts[i] = string.Concat(parts[i].AsSpan(0, 2), CapitalizeFirst(parts[i][2..], culture));
            }
        }
        return string.Join('-', parts);
    }

    private static string CapitalizeFirst(string text, CultureInfo culture)
    {
        var index = text.ToList().FindIndex(char.IsLetter);
        return index < 0
            ? text
            : string.Concat(text.AsSpan(0, index), text[index].ToString().ToUpper(culture), text.AsSpan(index + 1));
    }
}
