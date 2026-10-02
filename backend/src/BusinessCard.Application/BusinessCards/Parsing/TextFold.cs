using System.Globalization;
using System.Text;

namespace BusinessCard.Application.BusinessCards.Parsing;

/// <summary>
/// Case- and diacritic-insensitive form for matching labels and place names
/// ("İSTANBUL", "Istanbul", "istanbul" all fold to "istanbul").
/// </summary>
/// <remarks>
/// Neither invariant nor tr-TR lower-casing works alone: invariant leaves "İ"
/// unchanged, and tr-TR turns English "MOBILE" into "mobıle". So marks are
/// stripped first ("İ" → "I" + dot → "I"), then the text is lower-cased and
/// dotless ı is mapped to i, so both languages match.
/// </remarks>
internal static class TextFold
{
    public static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }
        return builder.ToString().ToLowerInvariant().Replace('ı', 'i');
    }
}
