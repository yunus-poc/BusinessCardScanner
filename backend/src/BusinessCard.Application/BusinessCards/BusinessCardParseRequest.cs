using BusinessCard.Domain.Cards;

namespace BusinessCard.Application.BusinessCards;

/// <summary>OCR lines with compact layout hints, as produced on the device. No image, no coordinates.</summary>
public sealed class BusinessCardParseRequest
{
    public IReadOnlyList<ParseLineDto>? Lines { get; init; }
}

public sealed class ParseLineDto
{
    public string? Text { get; init; }

    /// <summary>1-based reading order.</summary>
    public int Order { get; init; }

    // Nullable so a missing value is a validation error instead of silently
    // becoming the enum default (Top / Small).
    public LayoutRegion? Region { get; init; }

    public LayoutSize? Size { get; init; }

    public LayoutAlign Align { get; init; } = LayoutAlign.Left;

    public bool SameBlock { get; init; }

    public LayoutGap Gap { get; init; } = LayoutGap.None;
}

/// <summary>Request limits. A real card has 5–25 lines of well under 100 characters.</summary>
public static class ParseLimits
{
    public const int MaxLines = 80;
    public const int MaxLineLength = 300;
    public const int MaxTotalLength = 8_000;
}
