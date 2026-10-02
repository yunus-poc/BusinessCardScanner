using System.Linq.Expressions;
using System.Text.RegularExpressions;
using FluentValidation;

namespace BusinessCard.Application.Contacts;

/// <summary>The contact as confirmed by the user in the app. Mirrors the parse result's fields.</summary>
public sealed class SaveContactRequest
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? CompanyName { get; init; }
    public string? JobTitle { get; init; }
    public string? Department { get; init; }
    public string? Email { get; init; }
    public IReadOnlyList<string>? AdditionalEmails { get; init; }
    public string? MobilePhone { get; init; }
    public string? BusinessPhone { get; init; }
    public IReadOnlyList<string>? AdditionalPhones { get; init; }
    public string? Website { get; init; }
    public string? Street { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
    public string? Notes { get; init; }
}

public sealed record SaveContactResponse(string Id, string DisplayName);

/// <summary>A possible duplicate is a normal outcome for the user to decide on, so it is data, not an error.</summary>
public sealed record DuplicateCheckResponse(bool PossibleDuplicate, IReadOnlyList<DuplicateMatch> Matches);

public static class PhoneDefaults
{
    /// <summary>Region assumed for numbers written without a country code (Türkiye).</summary>
    public const string Region = "TR";
}

public static class ContactLimits
{
    public const int MaxFieldLength = 256;
    public const int MaxNotesLength = 2_000;
    public const int MaxAdditionalEmails = 2; // + primary = Graph's limit of 3
    public const int MaxAdditionalPhones = 4;
    public const int MaxContactIdLength = 512;
}

internal sealed partial class SaveContactRequestValidator : AbstractValidator<SaveContactRequest>
{
    public SaveContactRequestValidator()
    {
        RuleFor(x => x)
            .Must(HasIdentity)
            .WithName("contact")
            .WithMessage("Provide at least a name, company, email or phone number.");

        Text(x => x.FirstName);
        Text(x => x.LastName);
        Text(x => x.CompanyName);
        Text(x => x.JobTitle);
        Text(x => x.Department);
        Text(x => x.Street);
        Text(x => x.City);
        Text(x => x.State);
        Text(x => x.PostalCode);
        Text(x => x.Country);

        RuleFor(x => x.Email).Must(BeEmail).WithMessage("Must be a valid email address.");
        RuleForEach(x => x.AdditionalEmails).Must(BeEmail).WithMessage("Must be a valid email address.");
        RuleFor(x => x.AdditionalEmails).Must(l => l is null || l.Count <= ContactLimits.MaxAdditionalEmails)
            .WithMessage($"At most {ContactLimits.MaxAdditionalEmails} additional emails.");

        RuleFor(x => x.MobilePhone).Must(BePhone).WithMessage("Must be a valid phone number.");
        RuleFor(x => x.BusinessPhone).Must(BePhone).WithMessage("Must be a valid phone number.");
        RuleForEach(x => x.AdditionalPhones).Must(BePhone).WithMessage("Must be a valid phone number.");
        RuleFor(x => x.AdditionalPhones).Must(l => l is null || l.Count <= ContactLimits.MaxAdditionalPhones)
            .WithMessage($"At most {ContactLimits.MaxAdditionalPhones} additional phone numbers.");

        RuleFor(x => x.Website).Must(BeWebsite).WithMessage("Must be a valid http(s) website.");
        RuleFor(x => x.Notes).Must(n => n is null || (n.Length <= ContactLimits.MaxNotesLength && !NotesControl().IsMatch(n)))
            .WithMessage($"Must be at most {ContactLimits.MaxNotesLength} characters without control characters.");
    }

    private static bool HasIdentity(SaveContactRequest r) =>
        new[] { r.FirstName, r.LastName, r.CompanyName, r.Email, r.MobilePhone, r.BusinessPhone }
            .Any(v => !string.IsNullOrWhiteSpace(v));

    private static bool BeSafeText(string? value) =>
        value is null || (value.Length <= ContactLimits.MaxFieldLength && !Control().IsMatch(value));

    private static bool BeEmail(string? value) =>
        string.IsNullOrWhiteSpace(value) || (value.Length <= ContactLimits.MaxFieldLength && Email().IsMatch(value.Trim()));

    private static bool BePhone(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || (Phone().IsMatch(value.Trim()) && value.Count(char.IsDigit) is >= 6 and <= 17);

    private static bool BeWebsite(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }
        var text = value.Trim();
        var withScheme = text.Contains("://", StringComparison.Ordinal) ? text : "https://" + text;
        return text.Length <= ContactLimits.MaxFieldLength
            && !text.Contains(' ', StringComparison.Ordinal)
            && Uri.TryCreate(withScheme, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && uri.Host.Contains('.', StringComparison.Ordinal)
            && string.IsNullOrEmpty(uri.UserInfo);
    }

    private void Text(Expression<Func<SaveContactRequest, string?>> field) =>
        RuleFor(field).Must(BeSafeText).WithMessage($"Must be at most {ContactLimits.MaxFieldLength} characters without control characters.");

    [GeneratedRegex(@"[\u0000-\u001F\u007F]")]
    private static partial Regex Control();

    // Notes may contain line breaks and tabs.
    [GeneratedRegex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]")]
    private static partial Regex NotesControl();

    [GeneratedRegex(@"^[A-Za-z0-9._%+\-]+@[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?)*\.[A-Za-z]{2,24}$")]
    private static partial Regex Email();

    [GeneratedRegex(@"^\+?[0-9 ()\-./]{6,24}$")]
    private static partial Regex Phone();
}
