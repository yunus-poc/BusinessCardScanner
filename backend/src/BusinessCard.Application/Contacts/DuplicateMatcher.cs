using BusinessCard.Application.BusinessCards.Merging;
using BusinessCard.Application.BusinessCards.Parsing;
using BusinessCard.Domain.Contacts;
using PhoneNumbers;

namespace BusinessCard.Application.Contacts;

public enum DuplicateReason
{
    /// <summary>Strongest signal: an email address is the same.</summary>
    Email,

    /// <summary>The new mobile number equals the contact's mobile or a business phone.</summary>
    MobilePhone,

    /// <summary>Weakest: same first and last name and a matching company. A name alone never counts.</summary>
    NameAndCompany,
}

public sealed record DuplicateMatch(
    string Id,
    string DisplayName,
    string? CompanyName,
    string? Email,
    string? MobilePhone,
    IReadOnlyList<DuplicateReason> Reasons);

/// <summary>
/// Lightweight duplicate decision. Deliberately not a deduplication engine: exact
/// identifiers first (email, phone in E.164), then name + company as a secondary hint.
/// </summary>
internal static class DuplicateMatcher
{
    private const int MaxMatches = 5;
    private static readonly PhoneNumberUtil Phones = PhoneNumberUtil.GetInstance();

    public static IReadOnlyList<DuplicateMatch> Match(ContactDetails contact, IEnumerable<ExistingContact> candidates)
    {
        var emails = contact.Emails.Select(e => e.ToLowerInvariant()).ToHashSet();
        var mobile = E164(contact.MobilePhone);

        return candidates
            .DistinctBy(c => c.Id)
            .Select(candidate => (Candidate: candidate, Reasons: Reasons(contact, emails, mobile, candidate)))
            .Where(x => x.Reasons.Count > 0)
            .OrderBy(x => x.Reasons.Min())
            .ThenByDescending(x => x.Reasons.Count)
            .Take(MaxMatches)
            .Select(x => new DuplicateMatch(
                x.Candidate.Id,
                x.Candidate.DisplayName,
                x.Candidate.CompanyName,
                x.Candidate.Emails.Count > 0 ? x.Candidate.Emails[0] : null,
                x.Candidate.MobilePhone,
                x.Reasons))
            .ToList();
    }

    private static List<DuplicateReason> Reasons(ContactDetails contact, HashSet<string> emails, string? mobile, ExistingContact candidate)
    {
        var reasons = new List<DuplicateReason>();
        if (candidate.Emails.Any(e => emails.Contains(e.ToLowerInvariant())))
        {
            reasons.Add(DuplicateReason.Email);
        }
        if (mobile is not null && new[] { candidate.MobilePhone }.Concat(candidate.BusinessPhones).Select(E164).Contains(mobile))
        {
            reasons.Add(DuplicateReason.MobilePhone);
        }
        if (SameName(contact, candidate) && SameCompany(contact.CompanyName, candidate.CompanyName))
        {
            reasons.Add(DuplicateReason.NameAndCompany);
        }
        return reasons;
    }

    private static bool SameName(ContactDetails contact, ExistingContact candidate) =>
        contact.FirstName is not null && contact.LastName is not null
        && Same(contact.FirstName, candidate.FirstName)
        && Same(contact.LastName, candidate.LastName);

    /// <summary>"ACME" matches "ACME Technology A.Ş."; both sides must have a company.</summary>
    private static bool SameCompany(string? company, string? other)
    {
        if (company is null || other is null)
        {
            return false;
        }
        var a = Compact(company);
        var b = Compact(other);
        return a.Length >= 3 && b.Length >= 3 && (a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal));
    }

    private static bool Same(string a, string? b) => b is not null && TextFold.Fold(a.Trim()) == TextFold.Fold(b.Trim());

    private static string Compact(string text) => string.Concat(CardVocabulary.Words(text)).Replace(".", string.Empty, StringComparison.Ordinal);

    internal static string? E164(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }
        try
        {
            var parsed = Phones.Parse(phone, PhoneExtractor.DefaultRegion);
            return Phones.IsValidNumber(parsed) ? Phones.Format(parsed, PhoneNumberFormat.E164) : null;
        }
        catch (NumberParseException)
        {
            return null;
        }
    }
}
