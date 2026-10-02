using BusinessCard.Domain.Contacts;

namespace BusinessCard.Application.Contacts;

/// <summary>
/// The signed-in user's Outlook / Exchange Online contacts, via Microsoft Graph with
/// delegated permissions. Graph is the source of truth; nothing is stored locally.
/// </summary>
/// <remarks>
/// Implementations translate Graph failures into <see cref="Errors.AppException"/> with
/// GRAPH_* / CONTACT_NOT_FOUND codes and never expose Graph types to callers.
/// </remarks>
public interface IMicrosoftGraphContactService
{
    Task<SavedContact> CreateAsync(ContactDetails contact, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the provided fields onto an existing contact. Fields that are null are left
    /// as they are; emails and phones are merged, not replaced.
    /// </summary>
    Task<SavedContact> UpdateAsync(string contactId, ContactDetails contact, CancellationToken cancellationToken);

    /// <summary>
    /// Existing contacts that share an email, the mobile number, or first + last name with
    /// <paramref name="contact"/>. These are candidates only; <see cref="DuplicateMatcher"/>
    /// decides which are actual possible duplicates.
    /// </summary>
    Task<IReadOnlyList<ExistingContact>> FindCandidatesAsync(ContactDetails contact, CancellationToken cancellationToken);
}
