using BusinessCard.Application.Contacts;
using BusinessCard.Domain.Contacts;

namespace BusinessCard.UnitTests.Contacts;

public sealed class DuplicateMatcherTests
{
    private static readonly ContactDetails NewContact = new()
    {
        FirstName = "Ahmet",
        LastName = "Yılmaz",
        DisplayName = "Ahmet Yılmaz",
        CompanyName = "ACME Technology",
        Emails = ["ahmet.yilmaz@acme.com.tr", "satis@acme.com.tr"],
        MobilePhone = "+90 532 123 45 67",
    };

    [Fact]
    public void Same_email_is_a_duplicate_regardless_of_case()
    {
        var match = Assert.Single(DuplicateMatcher.Match(NewContact, [Existing("1", emails: ["SATIS@ACME.COM.TR"])]));

        Assert.Equal([DuplicateReason.Email], match.Reasons);
    }

    [Theory]
    [InlineData("05321234567")]
    [InlineData("+905321234567")]
    [InlineData("0 (532) 123 45 67")]
    public void Same_mobile_in_any_format_is_a_duplicate(string stored)
    {
        var match = Assert.Single(DuplicateMatcher.Match(NewContact, [Existing("1", mobile: stored)]));

        Assert.Equal([DuplicateReason.MobilePhone], match.Reasons);
    }

    [Fact]
    public void Mobile_stored_as_a_business_phone_also_matches()
    {
        var match = Assert.Single(DuplicateMatcher.Match(NewContact, [Existing("1", businessPhones: ["+90 532 123 45 67"])]));

        Assert.Equal([DuplicateReason.MobilePhone], match.Reasons);
    }

    [Theory]
    [InlineData("ACME")]
    [InlineData("Acme Technology A.Ş.")]
    [InlineData("ACME TECHNOLOGY")]
    public void Same_name_and_company_is_a_weak_duplicate(string company)
    {
        var match = Assert.Single(DuplicateMatcher.Match(NewContact, [Existing("1", first: "AHMET", last: "YILMAZ", company: company)]));

        Assert.Equal([DuplicateReason.NameAndCompany], match.Reasons);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("NOVA Enerji")]
    public void The_same_name_alone_is_never_a_duplicate(string? company)
    {
        // Common names collide; without a matching company this is someone else.
        Assert.Empty(DuplicateMatcher.Match(NewContact, [Existing("1", first: "Ahmet", last: "Yılmaz", company: company)]));
    }

    [Fact]
    public void Unrelated_candidates_are_dropped()
    {
        Assert.Empty(DuplicateMatcher.Match(NewContact, [Existing("1", emails: ["someone@else.com"], mobile: "+90 555 000 00 00")]));
    }

    [Fact]
    public void Strongest_evidence_first_and_candidates_from_several_queries_appear_once()
    {
        var byName = Existing("name", first: "Ahmet", last: "Yılmaz", company: "ACME");
        var byPhone = Existing("phone", mobile: "05321234567");
        var byEmailAndPhone = Existing("both", emails: ["ahmet.yilmaz@acme.com.tr"], mobile: "+905321234567");

        var matches = DuplicateMatcher.Match(NewContact, [byName, byPhone, byEmailAndPhone, byEmailAndPhone]);

        Assert.Equal(["both", "phone", "name"], matches.Select(m => m.Id));
        Assert.Equal([DuplicateReason.Email, DuplicateReason.MobilePhone], matches[0].Reasons);
    }

    private static ExistingContact Existing(
        string id,
        string? first = null,
        string? last = null,
        string? company = null,
        string[]? emails = null,
        string? mobile = null,
        string[]? businessPhones = null) => new()
        {
            Id = id,
            DisplayName = "Existing",
            FirstName = first,
            LastName = last,
            CompanyName = company,
            Emails = emails ?? [],
            MobilePhone = mobile,
            BusinessPhones = businessPhones ?? [],
        };
}
