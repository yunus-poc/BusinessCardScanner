using BusinessCard.Domain.Cards;

namespace BusinessCard.Application.BusinessCards.Merging;

/// <summary>
/// Deterministic best guess from text and layout alone, used when the LLM is
/// unavailable (and as a second opinion when it is not). Deliberately simple and
/// conservative: anything it fills is always marked for review.
/// </summary>
internal static class LayoutHeuristics
{
    private const double MinimumPersonScore = 2.0;

    /// <param name="lines">Semantic lines with residual text (deterministic values removed).</param>
    public static SemanticCandidates Classify(IReadOnlyList<CardLine> lines, CardContext context)
    {
        var used = new HashSet<int>();

        var address = lines.Where(l => CardVocabulary.LooksLikeAddress(l.Text)).Take(4).Select(l => l.Order).ToList();
        used.UnionWith(address);

        var person = lines
            .Where(l => !used.Contains(l.Order) && CardVocabulary.LooksLikePersonName(l.Text))
            .Select(l => (Line: l, Score: PersonScore(l, context)))
            .Where(x => x.Score >= MinimumPersonScore)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Line.Order)
            .Select(x => x.Line)
            .FirstOrDefault();
        if (person is not null)
        {
            used.Add(person.Order);
        }

        var title = person is null ? null : TitleAfter(person, lines, used);
        if (title is not null)
        {
            used.Add(title.Order);
        }

        var department = lines
            .Where(l => !used.Contains(l.Order) && CardVocabulary.HasDepartmentWord(l.Text))
            .Take(1)
            .ToList();
        used.UnionWith(department.Select(l => l.Order));

        var company = CompanyLine(lines, used, context);

        return new SemanticCandidates(
            CandidateSource.Layout,
            person is null ? [] : [person.Order],
            company is null ? [] : [company.Order],
            title is null ? [] : [title.Order],
            department.Select(l => l.Order).ToList(),
            address,
            City: null,
            State: null,
            Country: null,
            Corrections: 0);
    }

    /// <summary>Visual prominence plus the strongest signal: the email address spells the name.</summary>
    internal static double PersonScore(CardLine line, CardContext context)
    {
        var score = 1.0;
        score += line.Size switch { LayoutSize.Large => 2.0, LayoutSize.Normal => 0.5, _ => 0 };
        score += line.Region switch { LayoutRegion.Center => 1.0, LayoutRegion.Top => 0.5, _ => 0 };
        if (context.EmailMatchesName(line.Text))
        {
            score += 4.0; // stronger than any visual cue
        }
        return score;
    }

    /// <summary>A title keyword in the next two lines, or the same-block line right below the name.</summary>
    private static CardLine? TitleAfter(CardLine person, IReadOnlyList<CardLine> lines, HashSet<int> used)
    {
        var following = lines.Where(l => l.Order > person.Order && !used.Contains(l.Order)).OrderBy(l => l.Order).Take(2).ToList();
        return following.FirstOrDefault(l => CardVocabulary.HasTitleWord(l.Text))
            ?? following.Take(1).FirstOrDefault(l =>
                l.SameBlock
                && l.Size <= person.Size
                && !CardVocabulary.HasCompanyWord(l.Text)
                && !CardVocabulary.LooksLikeAddress(l.Text));
    }

    /// <summary>Legal/sector vocabulary, then the email/web domain, then the most prominent leftover line.</summary>
    private static CardLine? CompanyLine(IReadOnlyList<CardLine> lines, HashSet<int> used, CardContext context)
    {
        var free = lines.Where(l => !used.Contains(l.Order)).ToList();
        return free.FirstOrDefault(l => CardVocabulary.HasCompanyWord(l.Text))
            ?? free.FirstOrDefault(l => context.DomainMatchesCompany(l.Text))
            ?? free
                .Where(l => l.Size == LayoutSize.Large && !CardVocabulary.HasTitleWord(l.Text) && l.Text.Any(char.IsLetter))
                .OrderBy(l => l.Region)
                .FirstOrDefault();
    }
}
