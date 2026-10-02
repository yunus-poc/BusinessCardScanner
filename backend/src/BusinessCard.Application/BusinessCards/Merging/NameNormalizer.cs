using System.Text.RegularExpressions;
using BusinessCard.Application.BusinessCards.Parsing;

namespace BusinessCard.Application.BusinessCards.Merging;

internal sealed record PersonName(string First, string? Last, string Display);

/// <summary>
/// Splits a printed name into first/last. The last word is the surname, so multi-part
/// given names ("Mehmet Ali Yılmaz", common in Türkiye) stay together; surname particles
/// ("van", "de", "bin") join the surname. Honorifics and post-nominals are dropped:
/// they are not part of the contact's name fields.
/// </summary>
internal static partial class NameNormalizer
{
    private static readonly HashSet<string> Honorifics =
    [
        "dr", "prof", "doc", "av", "muh", "mr", "mrs", "ms", "miss", "sn", "sayin", "bay", "bayan", "uzm", "op", "ing", "eng",
    ];

    private static readonly HashSet<string> SurnameParticles = ["van", "von", "de", "da", "di", "del", "der", "den", "bin", "al", "el", "le", "la", "du", "ter"];

    public static PersonName? Normalize(string raw, bool turkish)
    {
        // ", PhD" / " - MBA" style post-nominals after a separator.
        var withoutSuffix = PostNominal().Replace(raw, string.Empty);
        var words = withoutSuffix.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .SkipWhile(w => Honorifics.Contains(TextFold.Fold(w).TrimEnd('.')))
            .Where(w => !TrailingDegrees.Contains(TextFold.Fold(w).Trim('.', ',')))
            .ToList();
        if (words.Count == 0)
        {
            return null;
        }

        var cased = TextCasing.PersonName(string.Join(' ', words), turkish).Split(' ');
        if (cased.Length == 1)
        {
            return new PersonName(cased[0], null, cased[0]);
        }

        var surnameStart = cased.Length - 1;
        while (surnameStart > 1 && SurnameParticles.Contains(TextFold.Fold(cased[surnameStart - 1])))
        {
            surnameStart--;
        }
        var first = string.Join(' ', cased[..surnameStart]);
        var last = string.Join(' ', cased[surnameStart..]);
        return new PersonName(first, last, $"{first} {last}");
    }

    private static readonly HashSet<string> TrailingDegrees = ["phd", "mba", "msc", "bsc", "cpa", "pmp", "jr", "sr", "md"];

    [GeneratedRegex(@"\s*[,|–-]\s*(?:ph\.?d\.?|mba|m\.?sc\.?|cpa|pmp|jr\.?|sr\.?|md)\b.*$", RegexOptions.IgnoreCase)]
    private static partial Regex PostNominal();
}
