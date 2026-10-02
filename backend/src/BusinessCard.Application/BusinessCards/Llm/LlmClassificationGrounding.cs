using System.Text.RegularExpressions;
using BusinessCard.Application.BusinessCards.Parsing;
using BusinessCard.Domain.Cards;

namespace BusinessCard.Application.BusinessCards.Llm;

/// <param name="Corrections">How many model claims were discarded; lowers confidence.</param>
internal sealed record GroundedClassification(LlmClassification Classification, int Corrections);

/// <summary>
/// Model-independent safety net: keeps only what the card actually supports.
/// A model that points at a line it was not given, uses a line twice, or names a
/// city that is not on the card has that claim removed. Constrained decoding
/// guarantees the JSON shape; this guarantees the content refers to real input.
/// </summary>
internal static class LlmClassificationGrounding
{
    public static GroundedClassification Ground(LlmClassification raw, IReadOnlyList<CardLine> sentLines)
    {
        var validIds = sentLines.Select(l => l.Order).ToHashSet();
        var used = new HashSet<int>();
        var corrections = 0;

        // Priority order resolves a line claimed twice: identity first, address last.
        IReadOnlyList<int> Keep(IReadOnlyList<int> ids)
        {
            var kept = new List<int>(ids.Count);
            foreach (var id in ids)
            {
                if (validIds.Contains(id) && used.Add(id))
                {
                    kept.Add(id);
                }
                else
                {
                    corrections++;
                }
            }
            return kept;
        }

        var person = Keep(raw.Person);
        var company = Keep(raw.Company);
        var title = Keep(raw.Title);
        var department = Keep(raw.Department);
        var address = Keep(raw.Address);

        var cardText = TextFold.Fold(string.Join('\n', sentLines.Select(l => l.Text)));
        string? OnCard(string? value)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return null;
            }
            var pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(TextFold.Fold(trimmed))}(?![\p{{L}}\p{{N}}])";
            if (Regex.IsMatch(cardText, pattern))
            {
                return trimmed;
            }
            corrections++;
            return null;
        }

        var grounded = new LlmClassification(
            person,
            company,
            title,
            department,
            address,
            OnCard(raw.City),
            OnCard(raw.State),
            OnCard(raw.Country));
        return new GroundedClassification(grounded, corrections);
    }
}
