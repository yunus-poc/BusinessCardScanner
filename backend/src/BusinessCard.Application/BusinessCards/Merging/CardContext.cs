using BusinessCard.Application.BusinessCards.Parsing;
using BusinessCard.Domain.Cards;

namespace BusinessCard.Application.BusinessCards.Merging;

/// <summary>Card-wide signals used for casing and cross-field checks.</summary>
internal sealed record CardContext(bool Turkish, IReadOnlyList<string> EmailNameTokens, string? CompanyDomainLabel)
{
    private static readonly HashSet<string> PublicSuffixParts =
    [
        "com", "net", "org", "edu", "gov", "co", "tr", "uk", "de", "eu", "io", "info", "biz", "fr", "it", "nl", "es", "ch",
        "at", "be", "us", "az", "ru", "ai", "app", "dev", "tech", "online", "bel", "gen", "av", "k12", "web", "tv",
    ];

    public static CardContext From(IReadOnlyList<CardLine> lines, DeterministicExtraction extraction)
    {
        var turkish =
            lines.Any(l => l.Text.Any(c => "çğıİöşüÇĞÖŞÜ".Contains(c, StringComparison.Ordinal)))
            || extraction.Phones.Any(p => p.E164.StartsWith("+90", StringComparison.Ordinal))
            || extraction.Emails.Any(e => e.Value.EndsWith(".tr", StringComparison.Ordinal))
            || extraction.Urls.Any(u => u.Value.Contains(".tr", StringComparison.Ordinal))
            || extraction.PostalCode is not null;

        var email = extraction.Emails.Count > 0 ? extraction.Emails[0].Value : null;
        var localTokens = email is null
            ? []
            : email[..email.IndexOf('@', StringComparison.Ordinal)]
                .Split(['.', '_', '-', '+'], StringSplitOptions.RemoveEmptyEntries)
                .Select(t => new string(t.Where(char.IsLetter).ToArray()))
                .Where(t => t.Length >= 2)
                .ToList();

        var host = extraction.Urls.FirstOrDefault(u => !u.IsSocial) is { } site
            ? new Uri(site.Value).Host
            : email?[(email.IndexOf('@', StringComparison.Ordinal) + 1)..];
        var label = host is null ? null : DomainLabel(host);

        return new CardContext(turkish, localTokens, label is null || CardVocabulary.IsGenericMailDomain(label) ? null : label);
    }

    /// <summary>"www.acme.com.tr" → "acme", "mail.nova-enerji.com" → "nova-enerji".</summary>
    internal static string? DomainLabel(string host)
    {
        var labels = host.ToLowerInvariant().Split('.').ToList();
        if (labels.Count > 0 && labels[0] == "www")
        {
            labels.RemoveAt(0);
        }
        while (labels.Count > 1 && PublicSuffixParts.Contains(labels[^1]))
        {
            labels.RemoveAt(labels.Count - 1);
        }
        return labels.Count == 0 ? null : labels[^1];
    }

    /// <summary>Email local part "ahmet.yilmaz" / "ayilmaz" matches "Ahmet Yılmaz".</summary>
    public bool EmailMatchesName(string name)
    {
        if (EmailNameTokens.Count == 0)
        {
            return false;
        }
        var nameTokens = CardVocabulary.Words(name).Where(w => w.Length >= 2).ToList();
        if (nameTokens.Count == 0)
        {
            return false;
        }
        if (EmailNameTokens.Any(t => t.Length >= 3 && nameTokens.Contains(t)))
        {
            return true;
        }
        // Initial + surname: "ayilmaz", "a.yilmaz" (the latter is split into ["a", "yilmaz"] above).
        var local = string.Concat(EmailNameTokens);
        return local == nameTokens[0][0] + nameTokens[^1] || local == nameTokens[0] + nameTokens[^1][0];
    }

    /// <summary>Company "NOVA ENERJİ A.Ş." matches domain label "nova-enerji" or "novaenerji".</summary>
    public bool DomainMatchesCompany(string company)
    {
        if (CompanyDomainLabel is null)
        {
            return false;
        }
        var label = CompanyDomainLabel.Replace("-", string.Empty, StringComparison.Ordinal);
        var compact = string.Concat(CardVocabulary.Words(company).Select(w => w.Replace(".", string.Empty, StringComparison.Ordinal)));
        var first = CardVocabulary.Words(company).FirstOrDefault();
        return label.Length >= 3
            && (compact.StartsWith(label, StringComparison.Ordinal) || label.StartsWith(compact, StringComparison.Ordinal) || first == label);
    }
}
