using System.Text.Json.Nodes;
using BusinessCard.Domain.Contacts;

namespace BusinessCard.Infrastructure.Graph;

/// <summary>
/// <see cref="ContactDetails"/> ↔ Graph <c>contact</c> JSON (v1.0). Kept in one place so
/// Graph's schema never leaks into the API contract.
/// </summary>
internal static class GraphContactMapper
{
    public const int MaxEmails = 3;

    /// <summary>Body for POST /me/contacts. Empty fields are omitted.</summary>
    public static JsonObject ToCreateBody(ContactDetails c)
    {
        var body = new JsonObject();
        Set(body, "givenName", c.FirstName);
        Set(body, "surname", c.LastName);
        Set(body, "displayName", c.DisplayName);
        // Outlook sorts by "File as"; "Surname, Given name" is Outlook's own default.
        Set(body, "fileAs", c.LastName is not null && c.FirstName is not null ? $"{c.LastName}, {c.FirstName}" : c.DisplayName);
        Set(body, "companyName", c.CompanyName);
        Set(body, "jobTitle", c.JobTitle);
        Set(body, "department", c.Department);
        Set(body, "mobilePhone", c.MobilePhone);
        Set(body, "businessHomePage", c.Website);
        Set(body, "personalNotes", c.Notes);

        if (c.Emails.Count > 0)
        {
            body["emailAddresses"] = Emails(c.Emails, c.DisplayName);
        }
        if (c.BusinessPhones.Count > 0)
        {
            body["businessPhones"] = new JsonArray(c.BusinessPhones.Select(p => (JsonNode)JsonValue.Create(p)).ToArray());
        }
        if (Address(c) is { } address)
        {
            body["businessAddress"] = address;
        }
        return body;
    }

    /// <summary>
    /// Body for PATCH /me/contacts/{id}: only provided fields are written, so data the card
    /// does not carry stays. Emails and phones are merged with the existing ones.
    /// </summary>
    public static JsonObject ToUpdateBody(ContactDetails c, IReadOnlyList<string> existingEmails, IReadOnlyList<string> existingBusinessPhones)
    {
        var body = ToCreateBody(c);

        var emails = c.Emails.Concat(existingEmails)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxEmails)
            .ToList();
        if (emails.Count > 0)
        {
            body["emailAddresses"] = Emails(emails, c.DisplayName);
        }

        var phones = c.BusinessPhones.Concat(existingBusinessPhones)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (phones.Count > 0)
        {
            body["businessPhones"] = new JsonArray(phones.Select(p => (JsonNode)JsonValue.Create(p)).ToArray());
        }
        return body;
    }

    /// <summary>Reads the fields needed for a merge from GET /me/contacts/{id}.</summary>
    public static (IReadOnlyList<string> Emails, IReadOnlyList<string> BusinessPhones) ReadMergeFields(JsonNode? contact)
    {
        var emails = (contact?["emailAddresses"] as JsonArray ?? [])
            .Select(e => e?["address"]?.GetValue<string>())
            .OfType<string>()
            .ToList();
        var phones = (contact?["businessPhones"] as JsonArray ?? [])
            .Select(p => p?.GetValue<string>())
            .OfType<string>()
            .ToList();
        return (emails, phones);
    }

    /// <summary>Reads a GET /me/contacts page ({"value":[...]}) into duplicate candidates.</summary>
    public static IReadOnlyList<ExistingContact> ReadCandidates(JsonNode? page)
    {
        var contacts = new List<ExistingContact>();
        foreach (var item in page?["value"] as JsonArray ?? [])
        {
            if (item?["id"]?.GetValue<string>() is not { } id)
            {
                continue;
            }
            var (emails, phones) = ReadMergeFields(item);
            contacts.Add(new ExistingContact
            {
                Id = id,
                DisplayName = item["displayName"]?.GetValue<string>() ?? string.Empty,
                FirstName = item["givenName"]?.GetValue<string>(),
                LastName = item["surname"]?.GetValue<string>(),
                CompanyName = item["companyName"]?.GetValue<string>(),
                Emails = emails,
                MobilePhone = item["mobilePhone"]?.GetValue<string>(),
                BusinessPhones = phones,
            });
        }
        return contacts;
    }

    public static SavedContact ReadSaved(JsonNode? contact, string fallbackDisplayName) => new(
        contact?["id"]?.GetValue<string>() ?? throw new InvalidOperationException("Graph response has no contact id."),
        contact["displayName"]?.GetValue<string>() ?? fallbackDisplayName);

    private static JsonArray Emails(IEnumerable<string> addresses, string displayName) =>
        new(addresses.Take(MaxEmails)
            .Select(a => (JsonNode)new JsonObject { ["address"] = a, ["name"] = displayName })
            .ToArray());

    private static JsonObject? Address(ContactDetails c)
    {
        var address = new JsonObject();
        Set(address, "street", c.Street);
        Set(address, "city", c.City);
        Set(address, "state", c.State);
        Set(address, "postalCode", c.PostalCode);
        Set(address, "countryOrRegion", c.Country);
        return address.Count == 0 ? null : address;
    }

    private static void Set(JsonObject target, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[name] = value;
        }
    }
}
