using BusinessCard.Application.BusinessCards.Parsing;
using BusinessCard.Application.Observability;
using BusinessCard.Domain.Contacts;
using PhoneNumbers;

namespace BusinessCard.Application.Contacts;

/// <summary>
/// Save use cases: normalize what the user confirmed, then write it to Outlook. The
/// request is already validated (FluentValidation filter); this only cleans and maps.
/// </summary>
public sealed class ContactsService(IMicrosoftGraphContactService graph, BusinessCardMetrics metrics)
{
    private static readonly PhoneNumberUtil Phones = PhoneNumberUtil.GetInstance();

    public async Task<SaveContactResponse> CreateAsync(SaveContactRequest request, CancellationToken cancellationToken)
    {
        var saved = await graph.CreateAsync(ToDetails(request), cancellationToken);
        return new SaveContactResponse(saved.Id, saved.DisplayName);
    }

    /// <summary>Checks the user's Outlook contacts for the contact about to be created.</summary>
    public async Task<DuplicateCheckResponse> CheckDuplicatesAsync(SaveContactRequest request, CancellationToken cancellationToken)
    {
        var details = ToDetails(request);
        var candidates = await graph.FindCandidatesAsync(details, cancellationToken);
        var matches = DuplicateMatcher.Match(details, candidates);
        metrics.DuplicateChecked(matches.Count > 0);
        return new DuplicateCheckResponse(matches.Count > 0, matches);
    }

    public async Task<SaveContactResponse> UpdateAsync(string contactId, SaveContactRequest request, CancellationToken cancellationToken)
    {
        var saved = await graph.UpdateAsync(contactId, ToDetails(request), cancellationToken);
        return new SaveContactResponse(saved.Id, saved.DisplayName);
    }

    internal static ContactDetails ToDetails(SaveContactRequest r)
    {
        var first = Clean(r.FirstName);
        var last = Clean(r.LastName);
        var company = Clean(r.CompanyName);

        var emails = new[] { r.Email }.Concat(r.AdditionalEmails ?? [])
            .Select(e => Clean(e)?.ToLowerInvariant())
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Take(1 + ContactLimits.MaxAdditionalEmails)
            .ToList();

        var mobile = NormalizePhone(r.MobilePhone);
        var businessPhones = new[] { r.BusinessPhone }.Concat(r.AdditionalPhones ?? [])
            .Select(NormalizePhone)
            .OfType<string>()
            .Where(p => p != mobile)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new ContactDetails
        {
            FirstName = first,
            LastName = last,
            DisplayName = DisplayName(first, last, company, emails, mobile ?? businessPhones.FirstOrDefault()),
            CompanyName = company,
            JobTitle = Clean(r.JobTitle),
            Department = Clean(r.Department),
            Emails = emails,
            MobilePhone = mobile,
            BusinessPhones = businessPhones,
            Website = NormalizeWebsite(r.Website),
            Street = Clean(r.Street),
            City = Clean(r.City),
            State = Clean(r.State),
            PostalCode = Clean(r.PostalCode),
            Country = Clean(r.Country),
            Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim(),
        };
    }

    /// <summary>Outlook lists contacts by display name, so there must always be one.</summary>
    private static string DisplayName(string? first, string? last, string? company, List<string> emails, string? phone)
    {
        var name = string.Join(' ', new[] { first, last }.OfType<string>());
        return name.Length > 0 ? name : company ?? (emails.Count > 0 ? emails[0] : phone ?? "Business card contact");
    }

    /// <summary>International format when parseable ("0532 123 45 67" → "+90 532 123 45 67"); else as typed.</summary>
    private static string? NormalizePhone(string? phone)
    {
        var text = Clean(phone);
        if (text is null)
        {
            return null;
        }
        try
        {
            var parsed = Phones.Parse(text, PhoneExtractor.DefaultRegion);
            return Phones.IsValidNumber(parsed) ? Phones.Format(parsed, PhoneNumberFormat.INTERNATIONAL) : text;
        }
        catch (NumberParseException)
        {
            return text;
        }
    }

    private static string? NormalizeWebsite(string? website)
    {
        var text = Clean(website);
        if (text is null)
        {
            return null;
        }
        return text.Contains("://", StringComparison.Ordinal) ? text : "https://" + text;
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var text = CardLineSanitizer.Clean(value);
        return text.Length == 0 ? null : text;
    }
}
