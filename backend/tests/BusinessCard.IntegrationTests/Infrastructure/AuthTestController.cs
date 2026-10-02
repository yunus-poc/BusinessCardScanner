using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BusinessCard.IntegrationTests.Infrastructure;

[ApiController]
[Route("test/auth")]
[SuppressMessage("Performance", "CA1822", Justification = "MVC only discovers instance action methods.")]
public sealed class AuthTestController : ControllerBase
{
    /// <summary>No attribute: protected only by the fallback policy.</summary>
    [HttpGet("fallback")]
    public IActionResult Fallback() => Ok();

    /// <summary>Plain [Authorize]: must still enforce the scope via the default policy.</summary>
    [Authorize]
    [HttpGet("authorize-attribute")]
    public IActionResult AuthorizeAttribute() => Ok();
}
