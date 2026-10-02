namespace BusinessCard.Domain.Contacts;

/// <summary>An Outlook contact returned as a duplicate candidate (only the fields needed to compare and show it).</summary>
public sealed record ExistingContact
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? CompanyName { get; init; }
    public IReadOnlyList<string> Emails { get; init; } = [];
    public string? MobilePhone { get; init; }
    public IReadOnlyList<string> BusinessPhones { get; init; } = [];
}
