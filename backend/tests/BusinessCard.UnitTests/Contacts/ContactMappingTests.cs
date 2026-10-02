using BusinessCard.Application.Contacts;
using BusinessCard.Domain.Contacts;
using BusinessCard.Infrastructure.Graph;

namespace BusinessCard.UnitTests.Contacts;

public sealed class ContactMappingTests
{
    [Fact]
    public void Graph_body_omits_empty_fields_and_nests_the_address()
    {
        var body = GraphContactMapper.ToCreateBody(new ContactDetails
        {
            FirstName = "Zeynep",
            LastName = "Kaya",
            DisplayName = "Zeynep Kaya",
            City = "Ankara",
            Notes = "Met at the fair",
        });

        Assert.Equal("Kaya, Zeynep", body["fileAs"]!.GetValue<string>());
        Assert.Equal("Met at the fair", body["personalNotes"]!.GetValue<string>());
        Assert.Equal("Ankara", body["businessAddress"]!["city"]!.GetValue<string>());
        Assert.False(body.ContainsKey("companyName"));
        Assert.False(body.ContainsKey("emailAddresses"));
        Assert.False(body["businessAddress"]!.AsObject().ContainsKey("street"));
    }

    [Fact]
    public void Graph_body_caps_emails_at_three()
    {
        var body = GraphContactMapper.ToCreateBody(new ContactDetails
        {
            DisplayName = "A",
            Emails = ["a@x.com", "b@x.com", "c@x.com", "d@x.com"],
        });

        Assert.Equal(3, body["emailAddresses"]!.AsArray().Count);
    }

    [Fact]
    public void Request_is_cleaned_and_normalized_before_saving()
    {
        var details = ContactsService.ToDetails(new SaveContactRequest
        {
            FirstName = "  Ahmet ",
            LastName = "Yılmaz​",
            Email = " Ahmet.Yilmaz@ACME.com.tr ",
            AdditionalEmails = ["ahmet.yilmaz@acme.com.tr", "satis@acme.com.tr"],
            MobilePhone = "0532 123 45 67",
            BusinessPhone = "+905321234567",
            AdditionalPhones = ["(0212) 555 00 00", "not-a-number-123456"],
            Website = "www.acme.com.tr",
            City = "",
        });

        Assert.Equal("Ahmet", details.FirstName);
        Assert.Equal("Yılmaz", details.LastName);
        Assert.Equal("Ahmet Yılmaz", details.DisplayName);
        Assert.Equal(["ahmet.yilmaz@acme.com.tr", "satis@acme.com.tr"], details.Emails);
        Assert.Equal("+90 532 123 45 67", details.MobilePhone);
        // The business phone equal to the mobile is dropped; unparseable input is kept as typed.
        Assert.Equal(["+90 212 555 00 00", "not-a-number-123456"], details.BusinessPhones);
        Assert.Equal("https://www.acme.com.tr", details.Website);
        Assert.Null(details.City);
    }

    [Theory]
    [InlineData(null, null, "NOVA", null, "NOVA")]
    [InlineData(null, "Kaya", null, null, "Kaya")]
    [InlineData(null, null, null, "info@nova.com", "info@nova.com")]
    public void Display_name_always_has_a_value(string? first, string? last, string? company, string? email, string expected)
    {
        var details = ContactsService.ToDetails(new SaveContactRequest { FirstName = first, LastName = last, CompanyName = company, Email = email });

        Assert.Equal(expected, details.DisplayName);
    }

    [Fact]
    public void Validator_accepts_a_typical_confirmed_contact()
    {
        var result = new SaveContactRequestValidator().Validate(new SaveContactRequest
        {
            FirstName = "Ahmet",
            Email = "ahmet@acme.com.tr",
            MobilePhone = "+90 532 123 45 67",
            Website = "acme.com.tr",
            Notes = "Line one\nLine two",
        });

        Assert.True(result.IsValid);
    }

    public static TheoryData<string, SaveContactRequest> InvalidContacts() => new()
    {
        { "no identity", new SaveContactRequest { City = "İstanbul", JobTitle = "CTO" } },
        { "bad email", new SaveContactRequest { FirstName = "A", Email = "ahmet@" } },
        { "bad extra email", new SaveContactRequest { FirstName = "A", AdditionalEmails = ["nope"] } },
        { "too many extra emails", new SaveContactRequest { FirstName = "A", AdditionalEmails = ["a@x.com", "b@x.com", "c@x.com"] } },
        { "bad phone", new SaveContactRequest { FirstName = "A", MobilePhone = "call me" } },
        { "bad website", new SaveContactRequest { FirstName = "A", Website = "javascript:alert(1)" } },
        { "website with credentials", new SaveContactRequest { FirstName = "A", Website = "https://u:p@evil.com" } },
        { "control character", new SaveContactRequest { FirstName = "A\u0007" } },
        { "too long", new SaveContactRequest { CompanyName = new string('x', ContactLimits.MaxFieldLength + 1) } },
        { "notes too long", new SaveContactRequest { FirstName = "A", Notes = new string('x', ContactLimits.MaxNotesLength + 1) } },
    };

    [Theory]
    [MemberData(nameof(InvalidContacts))]
    public void Validator_rejects_invalid_contacts(string scenario, SaveContactRequest request)
    {
        Assert.False(new SaveContactRequestValidator().Validate(request).IsValid, scenario);
    }
}
