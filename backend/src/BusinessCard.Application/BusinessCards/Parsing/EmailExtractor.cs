using System.Net.Mail;
using System.Text.RegularExpressions;

namespace BusinessCard.Application.BusinessCards.Parsing;

/// <param name="Repaired">True when OCR damage was fixed (spaces, comma for dot, Turkish letters).</param>
internal sealed record EmailMatch(string Value, bool Repaired);

internal static partial class EmailExtractor
{
    /// <summary>
    /// Finds email addresses, repairs common OCR damage, and returns the line with
    /// the addresses removed so later extractors do not see them again.
    /// </summary>
    public static (IReadOnlyList<EmailMatch> Emails, string Remaining) Extract(string text)
    {
        if (!text.Contains('@', StringComparison.Ordinal))
        {
            return ([], text);
        }

        // "ahmet . yilmaz @ acme , com" → "ahmet.yilmaz@acme.com"
        var repairedText = SpaceAroundAt().Replace(text, "@");
        repairedText = DomainSeparator().Replace(repairedText, ".");
        var spacingRepaired = repairedText != text;

        var emails = new List<EmailMatch>();
        var remaining = EmailCandidate().Replace(repairedText, match =>
        {
            var candidate = match.Value.TrimEnd('.', '-');
            var ascii = TextFold.Fold(candidate); // also maps Turkish letters to ASCII
            if (IsValid(ascii))
            {
                // Differs beyond case only when Turkish letters were transliterated.
                var transliterated = !string.Equals(ascii, candidate, StringComparison.OrdinalIgnoreCase);
                emails.Add(new EmailMatch(ascii, spacingRepaired || transliterated));
                return " ";
            }
            return match.Value;
        });

        return (emails, remaining);
    }

    private static bool IsValid(string email) =>
        StrictEmail().IsMatch(email)
        && MailAddress.TryCreate(email, out var parsed)
        && parsed.Address == email;

    // Letters include Turkish ones so OCR'd "yılmaz@" is caught and then repaired.
    [GeneratedRegex(@"[\p{L}0-9._%+\-]+@[\p{L}0-9\-]+(?:\.[\p{L}0-9\-]+)+")]
    private static partial Regex EmailCandidate();

    [GeneratedRegex(@"^[a-z0-9._%+\-]+@[a-z0-9](?:[a-z0-9\-]*[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9\-]*[a-z0-9])?)*\.[a-z]{2,24}$")]
    private static partial Regex StrictEmail();

    [GeneratedRegex(@"\s*@\s*")]
    private static partial Regex SpaceAroundAt();

    /// <summary>
    /// A comma, or a dot with spaces, inside the domain part, but only when a known
    /// TLD follows. Otherwise "a@b.com, Tel" would become "a@b.com.tel".
    /// </summary>
    [GeneratedRegex(
        @"(?<=@[\p{L}0-9.\-]+)(?:\s*,\s*|\s+\.\s*|\s*\.\s+)(?=(?:com|net|org|edu|gov|info|biz|io|co|tr|de|uk|eu)(?![\p{L}0-9]))",
        RegexOptions.IgnoreCase)]
    private static partial Regex DomainSeparator();
}
