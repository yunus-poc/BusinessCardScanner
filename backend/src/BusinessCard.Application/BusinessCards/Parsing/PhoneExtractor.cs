using System.Text.RegularExpressions;
using PhoneNumbers;

namespace BusinessCard.Application.BusinessCards.Parsing;

internal enum PhoneKind
{
    Mobile,
    Business,
    Fax,

    /// <summary>Valid number whose type cannot be determined (e.g. US numbers) and no label.</summary>
    Unknown,
}

/// <param name="E164">Canonical form for de-duplication and duplicate checks, e.g. "+905321234567".</param>
/// <param name="Display">International format for the contact, e.g. "+90 532 123 45 67".</param>
internal sealed record PhoneMatch(string E164, string Display, PhoneKind Kind);

/// <summary>
/// Finds phone numbers with libphonenumber (Türkiye as the default region), so
/// "0532 123 45 67", "(0212) 555 00 00", "0090 …", "+44 20 …" and numbers embedded
/// in "Tel: … Fax: …" lines are all parsed and validated by the same rules.
/// </summary>
internal static partial class PhoneExtractor
{
    public const string DefaultRegion = Contacts.PhoneDefaults.Region;

    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    private static readonly HashSet<string> MobileLabels = ["m", "mob", "mobile", "mobil", "gsm", "cep", "cell", "handy"];
    private static readonly HashSet<string> FaxLabels = ["f", "fax", "faks", "telefax", "telefaks"];
    private static readonly HashSet<string> BusinessLabels = ["t", "tel", "phone", "telefon", "office", "is", "o", "d", "direct", "p", "santral"];

    public static (IReadOnlyList<PhoneMatch> Phones, string Remaining) Extract(string text)
    {
        var matches = Util.FindNumbers(text, DefaultRegion, PhoneNumberUtil.Leniency.VALID, long.MaxValue).ToList();
        if (matches.Count == 0)
        {
            return ([], text);
        }

        var phones = new List<PhoneMatch>(matches.Count);
        var labelStart = 0;
        foreach (var match in matches)
        {
            var label = LastWord(text[labelStart..match.Start]);
            phones.Add(new PhoneMatch(
                Util.Format(match.Number, PhoneNumberFormat.E164),
                Util.Format(match.Number, PhoneNumberFormat.INTERNATIONAL),
                Classify(match.Number, label)));
            labelStart = match.Start + match.Length;
        }

        var remaining = text;
        foreach (var match in matches.OrderByDescending(m => m.Start))
        {
            remaining = string.Concat(remaining.AsSpan(0, match.Start), " ", remaining.AsSpan(match.Start + match.Length));
        }
        return (phones, remaining);
    }

    /// <remarks>
    /// The number's own type wins when it is definitive: people often print a mobile
    /// number after "Tel:". Only a fax label overrides it, and labels decide when the
    /// type is ambiguous (FIXED_LINE_OR_MOBILE, e.g. US numbers).
    /// </remarks>
    private static PhoneKind Classify(PhoneNumber number, string? label)
    {
        if (label is not null && FaxLabels.Contains(label))
        {
            return PhoneKind.Fax;
        }
        return Util.GetNumberType(number) switch
        {
            PhoneNumberType.MOBILE => PhoneKind.Mobile,
            PhoneNumberType.FIXED_LINE or PhoneNumberType.UAN or PhoneNumberType.TOLL_FREE => PhoneKind.Business,
            _ when label is not null && MobileLabels.Contains(label) => PhoneKind.Mobile,
            _ when label is not null && BusinessLabels.Contains(label) => PhoneKind.Business,
            _ => PhoneKind.Unknown,
        };
    }

    /// <summary>The last word before the number: "Tel:" → "tel", "İş /" → "is".</summary>
    private static string? LastWord(string labelText)
    {
        var words = Word().Matches(TextFold.Fold(labelText));
        return words.Count == 0 ? null : words[^1].Value;
    }

    [GeneratedRegex(@"[a-z]+")]
    private static partial Regex Word();
}
