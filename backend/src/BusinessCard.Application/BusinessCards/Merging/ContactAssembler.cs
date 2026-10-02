using BusinessCard.Application.BusinessCards.Parsing;
using BusinessCard.Domain.Cards;

namespace BusinessCard.Application.BusinessCards.Merging;

internal sealed record AssembledContact(
    ParsedContact Contact,
    IReadOnlyList<string> AdditionalEmails,
    IReadOnlyList<string> AdditionalPhones);

/// <summary>
/// Merges deterministic values and semantic candidates into the editable contact,
/// normalizes each field and scores it.
/// </summary>
/// <remarks>
/// Precedence: email, phones, website and postal code are deterministic only; the LLM
/// never sees them. Semantic fields come from the LLM, or from layout heuristics when it
/// failed. Confidence starts from a per-source base and moves with independent evidence
/// (layout, email/domain agreement, LLM vs layout agreement, grounding corrections).
/// It is a ranking heuristic, not a probability.
/// </remarks>
internal static class ContactAssembler
{
    private const int MaxEmails = 3; // Graph limit per contact

    private const double LlmBase = 0.80;
    private const double LayoutBase = 0.55;

    public static AssembledContact Assemble(
        DeterministicExtraction extraction,
        IReadOnlyList<CardLine> semanticLines,
        SemanticCandidates? llm,
        SemanticCandidates layout,
        CardContext context,
        double reviewThreshold)
    {
        var scorer = new Scorer(reviewThreshold);
        var candidates = llm ?? layout;
        var fromLayout = candidates.Source == CandidateSource.Layout;
        var baseScore = fromLayout ? LayoutBase : LlmBase;
        var corrected = candidates.Corrections > 0 ? -0.05 : 0;
        var lines = semanticLines.ToDictionary(l => l.Order);
        string? Text(IReadOnlyList<int> ids, string separator) =>
            ids.Count == 0 ? null : string.Join(separator, ids.Select(id => lines[id].Text));
        bool Agrees(Func<SemanticCandidates, IReadOnlyList<int>> field) =>
            !fromLayout && field(candidates).Count > 0 && field(candidates).SequenceEqual(field(layout));

        // Person
        var personText = Text(candidates.Person, " ");
        var name = personText is null ? null : NameNormalizer.Normalize(personText, context.Turkish);
        var personScore = baseScore + corrected
            + Bonus(candidates.Person.Any(id => lines[id].Size == LayoutSize.Large || lines[id].Region == LayoutRegion.Center), 0.08)
            + Bonus(personText is not null && !CardVocabulary.LooksLikePersonName(personText), -0.10)
            + Bonus(personText is not null && context.EmailMatchesName(personText), 0.10)
            + Bonus(Agrees(c => c.Person), 0.05)
            + Bonus(name is { Last: null }, -0.10);

        // Company, title, department
        var companyText = Text(candidates.Company, " ");
        var companyScore = baseScore + corrected
            + Bonus(companyText is not null && context.DomainMatchesCompany(companyText), 0.10)
            + Bonus(companyText is not null && CardVocabulary.HasCompanyWord(companyText), 0.04)
            + Bonus(Agrees(c => c.Company), 0.05);

        var titleText = Text(candidates.Title, " ");
        var titleScore = baseScore - 0.02 + corrected
            + Bonus(titleText is not null && CardVocabulary.HasTitleWord(titleText), 0.08)
            + Bonus(candidates.Person.Count > 0 && candidates.Title.Count > 0 && candidates.Title[0] == candidates.Person[^1] + 1, 0.05)
            + Bonus(Agrees(c => c.Title), 0.05);

        var departmentText = Text(candidates.Department, " ");
        var departmentScore = baseScore - 0.05 + corrected
            + Bonus(departmentText is not null && CardVocabulary.HasDepartmentWord(departmentText), 0.08);

        // Address: city first, because the street drops the city name.
        var postal = extraction.PostalCode;
        var (city, cityScore, cityForceReview) = City(candidates, postal, context, baseScore + corrected);
        var street = AddressNormalizer.Street(candidates.Address.Select(id => lines[id].Text), postal?.Value, city);
        var streetScore = baseScore - 0.05 + corrected
            + Bonus(candidates.Address.Count > 0 && candidates.Address.All(id => CardVocabulary.LooksLikeAddress(lines[id].Text)), 0.08)
            + Bonus(Agrees(c => c.Address), 0.05);
        var state = candidates.State is null ? null : TextCasing.Place(candidates.State);
        var (country, countryScore, countryForceReview) = Country(candidates, city, postal, context);

        // Deterministic values
        var mobile = extraction.Phones.FirstOrDefault(p => p.Kind == PhoneKind.Mobile);
        var business = extraction.Phones.FirstOrDefault(p => p.Kind == PhoneKind.Business)
            ?? extraction.Phones.FirstOrDefault(p => p.Kind == PhoneKind.Unknown);
        var email = extraction.Emails.Count > 0 ? extraction.Emails[0] : null;
        var website = extraction.Urls.FirstOrDefault(u => !u.IsSocial);
        var social = extraction.Urls.Where(u => u.IsSocial).Select(u => u.Value).ToList();

        var contact = new ParsedContact
        {
            FirstName = scorer.Field(name?.First, personScore, fromLayout),
            LastName = scorer.Field(name?.Last, personScore, fromLayout),
            DisplayName = scorer.Field(name?.Display, personScore, fromLayout),
            CompanyName = scorer.Field(companyText is null ? null : TextCasing.Company(companyText), companyScore, fromLayout),
            JobTitle = scorer.Field(titleText is null ? null : TextCasing.Title(titleText), titleScore, fromLayout),
            Department = scorer.Field(departmentText is null ? null : TextCasing.Title(departmentText), departmentScore, fromLayout),
            Street = scorer.Field(street, streetScore, fromLayout),
            City = scorer.Field(city, cityScore, cityForceReview),
            State = scorer.Field(state, baseScore - 0.05, fromLayout),
            Country = scorer.Field(country, countryScore, countryForceReview),
            PostalCode = scorer.Field(postal?.Value, postal is { Corroborated: true } ? 0.9 : 0.75, false),
            Email = scorer.Field(email?.Value, email is { Repaired: true } ? 0.7 : 0.97, false),
            MobilePhone = scorer.Field(mobile?.Display, 0.95, false),
            // Unknown type (no label, ambiguous number plan): plausible, but worth a look.
            BusinessPhone = scorer.Field(business?.Display, business?.Kind == PhoneKind.Business ? 0.9 : 0.7, false),
            Website = scorer.Field(website?.Value, 0.92, false),
            Notes = scorer.Field(social.Count == 0 ? null : string.Join('\n', social), 0.9, false),
        };

        var additionalEmails = extraction.Emails.Skip(1).Take(MaxEmails - 1).Select(e => e.Value).ToList();
        var additionalPhones = extraction.Phones
            .Where(p => p.Kind != PhoneKind.Fax && p != mobile && p != business)
            .Select(p => p.Display)
            .ToList();

        return new AssembledContact(contact, additionalEmails, additionalPhones);
    }

    /// <summary>LLM city (written on the card) checked against the postal-code province.</summary>
    private static (string? Value, double Score, bool ForceReview) City(
        SemanticCandidates candidates, PostalMatch? postal, CardContext context, double baseScore)
    {
        var hint = postal?.CityHint;
        if (candidates.City is { } llmCity)
        {
            var value = AddressNormalizer.City(llmCity);
            var agreement = hint is null ? 0 : TextFold.Fold(hint) == TextFold.Fold(value) ? 0.12 : -0.25;
            return (value, baseScore + agreement, candidates.Source == CandidateSource.Layout);
        }
        return hint is null
            ? (null, 0, false)
            : (hint, postal!.Corroborated ? 0.85 : 0.6, !postal.Corroborated);
    }

    /// <summary>Country as written on the card, else inferred from a Turkish province (flagged unless corroborated).</summary>
    private static (string? Value, double Score, bool ForceReview) Country(
        SemanticCandidates candidates, string? city, PostalMatch? postal, CardContext context)
    {
        if (candidates.Country is { } written)
        {
            return (AddressNormalizer.Country(written), 0.88, candidates.Source == CandidateSource.Layout);
        }
        var cityIsTurkishProvince = city is not null && TurkishProvinces.FindIn(city) == city;
        if (!cityIsTurkishProvince)
        {
            return (null, 0, false);
        }
        var corroborated = postal is { Corroborated: true };
        return (TurkishProvinces.Country, corroborated ? 0.85 : 0.7, !corroborated);
    }

    private static double Bonus(bool condition, double delta) => condition ? delta : 0;

    private sealed class Scorer(double reviewThreshold)
    {
        public ParsedField Field(string? value, double score, bool forceReview)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return ParsedField.Empty;
            }
            var confidence = Math.Round(Math.Clamp(score, 0, 0.99), 2);
            return new ParsedField(value, confidence, forceReview || confidence < reviewThreshold);
        }
    }
}
