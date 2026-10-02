using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BusinessCard.Domain.Cards;

namespace BusinessCard.Application.BusinessCards.Parsing;

/// <summary>
/// Cleans untrusted OCR text before any parsing: Unicode normalization, removal of
/// control/format characters (including bidi overrides and zero-width characters),
/// whitespace collapsing, and OCR-noise filtering.
/// </summary>
internal static partial class CardLineSanitizer
{
    public static IReadOnlyList<CardLine> Sanitize(IEnumerable<ParseLineDto> lines)
    {
        return lines
            .OrderBy(l => l.Order)
            .Select(l => (Line: l, Text: Clean(l.Text ?? string.Empty)))
            .Where(x => x.Text.Length > 0 && !OcrNoiseFilter.IsNoise(x.Text))
            .Select(x => new CardLine(
                x.Text,
                x.Line.Order,
                x.Line.Region!.Value,
                x.Line.Size!.Value,
                x.Line.Align,
                x.Line.SameBlock,
                x.Line.Gap))
            .ToList();
    }

    public static string Clean(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormC);
        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format
                or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse
                or UnicodeCategory.OtherNotAssigned)
            {
                // Tabs and newlines become spaces; everything else invisible is dropped.
                if (char.IsWhiteSpace(c))
                {
                    builder.Append(' ');
                }
                continue;
            }
            builder.Append(c);
        }
        return Whitespace().Replace(builder.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>Conservative filter for logo fragments and OCR artefacts. When in doubt, keep the line.</summary>
internal static partial class OcrNoiseFilter
{
    public static bool IsNoise(string text)
    {
        var letterOrDigit = text.Count(char.IsLetterOrDigit);
        if (letterOrDigit == 0)
        {
            return true; // "|||", "— — —", "•"
        }
        if (text.Length < 2)
        {
            return true; // stray single character
        }
        if (RepeatedCharacter().IsMatch(text))
        {
            return true; // "IIIIII", "llll", "0000" from logo stripes or borders
        }
        // Mostly symbols: "|| ® ||", "=/=/=" (but not "A.Ş." or "+90 ...").
        return (double)letterOrDigit / text.Length < 0.4;
    }

    [GeneratedRegex(@"^(.)\1{3,}$")]
    private static partial Regex RepeatedCharacter();
}
