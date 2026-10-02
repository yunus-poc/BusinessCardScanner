using BusinessCard.Application.BusinessCards;
using BusinessCard.Domain.Cards;

namespace BusinessCard.UnitTests.Parsing;

public sealed class ParseRequestValidatorTests
{
    private readonly BusinessCardParseRequestValidator _validator = new();

    [Fact]
    public void Minimal_request_from_the_spec_is_valid()
    {
        var request = new BusinessCardParseRequest { Lines = [Line(1, "Ahmet Yılmaz")] };

        Assert.True(_validator.Validate(request).IsValid);
    }

    public static TheoryData<string, BusinessCardParseRequest> InvalidRequests() => new()
    {
        { "no lines", new BusinessCardParseRequest { Lines = [] } },
        { "null lines", new BusinessCardParseRequest() },
        { "too many lines", new BusinessCardParseRequest { Lines = [.. Enumerable.Range(1, ParseLimits.MaxLines + 1).Select(i => Line(i, "x" + i))] } },
        { "line too long", new BusinessCardParseRequest { Lines = [Line(1, new string('a', ParseLimits.MaxLineLength + 1))] } },
        { "total too long", new BusinessCardParseRequest { Lines = [.. Enumerable.Range(1, 30).Select(i => Line(i, new string('a', 290)))] } },
        { "empty text", new BusinessCardParseRequest { Lines = [Line(1, "  ")] } },
        { "duplicate order", new BusinessCardParseRequest { Lines = [Line(1, "a"), Line(1, "b")] } },
        { "order zero", new BusinessCardParseRequest { Lines = [Line(0, "a")] } },
        { "missing region", new BusinessCardParseRequest { Lines = [new ParseLineDto { Text = "a", Order = 1, Size = LayoutSize.Normal }] } },
        { "missing size", new BusinessCardParseRequest { Lines = [new ParseLineDto { Text = "a", Order = 1, Region = LayoutRegion.Top }] } },
        { "undefined enum", new BusinessCardParseRequest { Lines = [new ParseLineDto { Text = "a", Order = 1, Region = (LayoutRegion)42, Size = LayoutSize.Normal }] } },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public void Rejects_invalid_requests(string scenario, BusinessCardParseRequest request)
    {
        Assert.False(_validator.Validate(request).IsValid, scenario);
    }

    private static ParseLineDto Line(int order, string text) => new()
    {
        Text = text,
        Order = order,
        Region = LayoutRegion.Center,
        Size = LayoutSize.Normal,
    };
}
