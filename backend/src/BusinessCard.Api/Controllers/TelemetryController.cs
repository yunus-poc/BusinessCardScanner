using BusinessCard.Application.Observability;
using Microsoft.AspNetCore.Mvc;

namespace BusinessCard.Api.Controllers;

/// <summary>Receives on-device timings (OCR, OCR → preview) so latency targets are measured, not assumed.</summary>
[ApiController]
[Route("api/telemetry")]
public sealed class TelemetryController(BusinessCardMetrics metrics) : ControllerBase
{
    [HttpPost("scan")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public IActionResult Scan(ScanTelemetryRequest request)
    {
        metrics.ClientScanReported(request.OcrMs, request.OcrToPreviewMs, request.LlmStatus!);
        return NoContent();
    }
}
