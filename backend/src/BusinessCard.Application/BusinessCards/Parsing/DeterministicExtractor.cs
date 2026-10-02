using System.Text.RegularExpressions;
using BusinessCard.Domain.Cards;

namespace BusinessCard.Application.BusinessCards.Parsing;

internal sealed record DeterministicExtraction(
    IReadOnlyList<EmailMatch> Emails,
    IReadOnlyList<PhoneMatch> Phones,
    IReadOnlyList<UrlMatch> Urls,
    PostalMatch? PostalCode,
    /// <summary>Lines that held only email/phone/URL values plus labels; the LLM does not need them.</summary>
    IReadOnlySet<int> ConsumedLineOrders,
    /// <summary>
    /// Per line order: the text with extracted emails, URLs and phones (and their labels)
    /// removed. "Ahmet Yılmaz | M: 0532 …" → "Ahmet Yılmaz". This is what the LLM sees and
    /// what semantic fields are built from, so a validated value can never leak into a name.
    /// </summary>
    IReadOnlyDictionary<int, string> Residuals);

/// <summary>
/// Everything that can be extracted reliably without AI. Per line, extractors run in a
/// fixed order (email → URL → phone → postal code) and each sees only the text left
/// by the previous one, so an email domain never becomes a website and phone digits
/// never become a postal code.
/// </summary>
internal static partial class DeterministicExtractor
{
    public static DeterministicExtraction Extract(IReadOnlyList<CardLine> lines)
    {
        var emails = new List<EmailMatch>();
        var phones = new List<PhoneMatch>();
        var urls = new List<UrlMatch>();
        PostalMatch? postal = null;
        var consumed = new HashSet<int>();
        var residuals = new Dictionary<int, string>();

        foreach (var line in lines)
        {
            var (lineEmails, afterEmails) = EmailExtractor.Extract(line.Text);
            var (lineUrls, afterUrls) = UrlExtractor.Extract(afterEmails);
            var (linePhones, afterPhones) = PhoneExtractor.Extract(afterUrls);
            postal ??= PostalCodeExtractor.Extract(afterPhones);

            AddDistinct(emails, lineEmails, e => e.Value);
            AddDistinct(urls, lineUrls, u => u.Value);
            AddDistinct(phones, linePhones, p => p.E164);

            var foundSomething = lineEmails.Count + lineUrls.Count + linePhones.Count > 0;
            if (foundSomething && IsOnlyLabels(afterPhones))
            {
                consumed.Add(line.Order);
            }
            residuals[line.Order] = foundSomething ? Residual(afterPhones) : line.Text;
        }

        return new DeterministicExtraction(emails, phones, urls, postal, consumed, residuals);
    }

    /// <summary>Removes "Tel:", "E-mail:", "M." style labels and leftover separators.</summary>
    private static string Residual(string remaining)
    {
        var withoutLabels = LabelPrefix().Replace(remaining, " ");
        var withoutSeparators = Separator().Replace(withoutLabels, " ");
        // A trailing '.' is kept: it belongs to "A.Ş." or "Ltd.".
        return Whitespace().Replace(withoutSeparators, " ").Trim(' ', ',', '/', '-', '–', ':');
    }

    [GeneratedRegex(@"(?<!\p{L})(?:e-?mail|e-?posta|telefon|tel|phone|gsm|cep|mobile|mobil|mob|fax|faks|web|[tmfewp])\s*[:.]", RegexOptions.IgnoreCase)]
    private static partial Regex LabelPrefix();

    [GeneratedRegex(@"\s*[|•·;]\s*")]
    private static partial Regex Separator();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private static void AddDistinct<T>(List<T> target, IEnumerable<T> items, Func<T, string> key)
    {
        foreach (var item in items)
        {
            if (!target.Exists(existing => key(existing) == key(item)))
            {
                target.Add(item);
            }
        }
    }

    /// <summary>"Tel: / Fax:", "E-mail", "|" left over: nothing a person or company name could be.</summary>
    private static bool IsOnlyLabels(string remaining)
    {
        var words = Word().Matches(TextFold.Fold(remaining)).Select(m => m.Value).ToList();
        return words.Count == 0 || words.All(LabelWords.Contains);
    }

    private static readonly HashSet<string> LabelWords =
    [
        "t", "m", "f", "e", "w", "p", "o", "d", "tel", "telefon", "phone", "mobile", "mobil", "mob", "gsm", "cep",
        "cell", "fax", "faks", "email", "mail", "eposta", "posta", "e", "web", "website", "site", "www", "url",
        "office", "is", "direct", "santral", "ext", "dahili", "x", "and", "ve",
    ];

    [GeneratedRegex(@"[a-z]+")]
    private static partial Regex Word();
}
