namespace BusinessCard.Domain.Cards;

/// <summary>Vertical position of a line within the card's text area.</summary>
public enum LayoutRegion
{
    Top,
    Center,
    Bottom,
}

/// <summary>Line height relative to the card's median line height.</summary>
public enum LayoutSize
{
    Small,
    Normal,
    Large,
}

public enum LayoutAlign
{
    Left,
    Center,
    Right,
}

/// <summary>Vertical space before the line; Large also marks a column break.</summary>
public enum LayoutGap
{
    None,
    Small,
    Large,
}

/// <summary>
/// One sanitized OCR line with its compact layout hints. Produced by the mobile
/// app's layout extractor; contains no pixel geometry.
/// </summary>
public sealed record CardLine(
    string Text,
    int Order,
    LayoutRegion Region,
    LayoutSize Size,
    LayoutAlign Align,
    bool SameBlock,
    LayoutGap Gap);
