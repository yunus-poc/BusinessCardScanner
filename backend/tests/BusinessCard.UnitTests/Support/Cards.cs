using BusinessCard.Application.BusinessCards;
using BusinessCard.Application.BusinessCards.Llm;
using BusinessCard.Application.Observability;
using BusinessCard.Domain.Cards;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BusinessCard.UnitTests.Support;

internal static class Cards
{
    public static HybridBusinessCardIntelligenceService Service(
        IBusinessCardLlmClassifier? llm = null,
        double reviewThreshold = 0.80,
        BusinessCardMetrics? metrics = null) =>
        new(
            llm ?? new FakeLlmClassifier(),
            Options.Create(new ParsingOptions { ReviewThreshold = reviewThreshold }),
            metrics ?? SharedMetrics.Value.Metrics,
            NullLogger<HybridBusinessCardIntelligenceService>.Instance);

    /// <summary>For tests that do not assert on metrics.</summary>
    internal static readonly Lazy<MetricRecorder> SharedMetrics = new(() => new MetricRecorder());

    /// <summary>Lines with explicit layout: (text, region, size).</summary>
    public static BusinessCardParseRequest Request(params (string Text, LayoutRegion Region, LayoutSize Size)[] lines) => new()
    {
        Lines = lines.Select((l, i) => new ParseLineDto { Text = l.Text, Order = i + 1, Region = l.Region, Size = l.Size }).ToList(),
    };

    /// <summary>Lines without meaningful layout (all center/normal).</summary>
    public static BusinessCardParseRequest Request(IEnumerable<string> texts) =>
        Request(texts.Select(t => (t, LayoutRegion.Center, LayoutSize.Normal)).ToArray());

    public static CardLine Line(int order, string text, LayoutRegion region = LayoutRegion.Center, LayoutSize size = LayoutSize.Normal, bool sameBlock = false) =>
        new(text, order, region, size, LayoutAlign.Left, sameBlock, LayoutGap.None);
}
