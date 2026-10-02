using System.Diagnostics.CodeAnalysis;
using BusinessCard.Application.Errors;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BusinessCard.IntegrationTests.Infrastructure;

[ApiController]
[AllowAnonymous]
[Route("test/pipeline")]
[SuppressMessage("Performance", "CA1822", Justification = "MVC only discovers instance action methods.")]
public sealed class PipelineTestController : ControllerBase
{
    public const string UnexpectedSecret = "internal-secret-detail";

    [HttpGet("app-error/{code}")]
    public IActionResult ThrowAppError(string code) =>
        throw new AppException(code, "Safe user-facing message.")
        {
            RetryAfter = code == ErrorCodes.GraphRateLimited ? TimeSpan.FromSeconds(6.2) : null,
        };

    [HttpGet("unexpected")]
    public IActionResult ThrowUnexpected() => throw new InvalidOperationException(UnexpectedSecret);

    [HttpPost("echo")]
    public ActionResult<EchoRequest> Echo(EchoRequest request) => request;
}

public sealed record EchoRequest(string? DisplayName, IReadOnlyList<EchoItem>? Items);

public sealed record EchoItem(string? Text);

public sealed class EchoRequestValidator : AbstractValidator<EchoRequest>
{
    public EchoRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty();
        RuleForEach(x => x.Items).ChildRules(item => item.RuleFor(i => i.Text).MaximumLength(5));
    }
}
