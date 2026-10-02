using System.Text.RegularExpressions;
using BusinessCard.Application.Contacts;
using Microsoft.AspNetCore.Mvc;

namespace BusinessCard.Api.Controllers;

/// <summary>Writes confirmed contacts to the signed-in user's Outlook contacts. Nothing is stored here.</summary>
[ApiController]
[Route("api/contacts")]
public sealed partial class ContactsController(ContactsService contacts) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<SaveContactResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<SaveContactResponse>> Create(SaveContactRequest request, CancellationToken cancellationToken)
    {
        var saved = await contacts.CreateAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, saved);
    }

    /// <summary>
    /// Looks for the contact in the user's Outlook before creating it. Always 200: a possible
    /// duplicate is a normal outcome the user decides on (update existing / create anyway / cancel).
    /// </summary>
    [HttpPost("check-duplicate")]
    [ProducesResponseType<DuplicateCheckResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DuplicateCheckResponse>> CheckDuplicate(SaveContactRequest request, CancellationToken cancellationToken)
    {
        return await contacts.CheckDuplicatesAsync(request, cancellationToken);
    }

    /// <summary>Updates an existing contact (explicit user choice after a duplicate warning).</summary>
    [HttpPut("{id}")]
    [ProducesResponseType<SaveContactResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SaveContactResponse>> Update(string id, SaveContactRequest request, CancellationToken cancellationToken)
    {
        if (id.Length > ContactLimits.MaxContactIdLength || !ContactId().IsMatch(id))
        {
            ModelState.AddModelError("id", "Not a valid contact id.");
            return ValidationProblem(ModelState);
        }
        return await contacts.UpdateAsync(id, request, cancellationToken);
    }

    [GeneratedRegex(@"^[A-Za-z0-9\-_=]+$")]
    private static partial Regex ContactId();
}
