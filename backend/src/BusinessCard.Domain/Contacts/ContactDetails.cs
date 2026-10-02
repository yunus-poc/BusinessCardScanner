namespace BusinessCard.Domain.Contacts;

/// <summary>
/// A confirmed, normalized contact ready to be stored. Independent of Microsoft Graph's
/// schema; the Graph integration maps it. Null means "not provided".
/// </summary>
public sealed record ContactDetails
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public required string DisplayName { get; init; }
    public string? CompanyName { get; init; }
    public string? JobTitle { get; init; }
    public string? Department { get; init; }

    /// <summary>Primary first; at most three (Graph's limit).</summary>
    public IReadOnlyList<string> Emails { get; init; } = [];

    public string? MobilePhone { get; init; }

    /// <summary>Primary first.</summary>
    public IReadOnlyList<string> BusinessPhones { get; init; } = [];

    public string? Website { get; init; }
    public string? Street { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
    public string? Notes { get; init; }
}

/// <param name="Id">Opaque Graph contact id.</param>
public sealed record SavedContact(string Id, string DisplayName);
