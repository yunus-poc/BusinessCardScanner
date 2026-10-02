using BusinessCard.Application.BusinessCards;
using Microsoft.AspNetCore.Mvc;

namespace BusinessCard.Api.Controllers;

[ApiController]
[Route("api/business-cards")]
public sealed class BusinessCardsController(IBusinessCardIntelligenceService intelligence) : ControllerBase
{
    /// <summary>Parses OCR lines with layout hints into an editable contact. Nothing is stored.</summary>
    [HttpPost("parse")]
    [ProducesResponseType<BusinessCardParseResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BusinessCardParseResult>> Parse(
        BusinessCardParseRequest request,
        CancellationToken cancellationToken)
    {
        return await intelligence.ParseAsync(request, cancellationToken);
    }
}
