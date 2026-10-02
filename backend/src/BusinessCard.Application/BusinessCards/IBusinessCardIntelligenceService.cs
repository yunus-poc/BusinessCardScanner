namespace BusinessCard.Application.BusinessCards;

/// <summary>Turns OCR lines with layout hints into an editable contact.</summary>
public interface IBusinessCardIntelligenceService
{
    Task<BusinessCardParseResult> ParseAsync(BusinessCardParseRequest request, CancellationToken cancellationToken);
}
