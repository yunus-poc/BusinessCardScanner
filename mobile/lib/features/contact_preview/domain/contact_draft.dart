import 'contact_validation.dart';
import 'parsed_card.dart';

/// The contact as the user is editing it. Immutable; every edit returns a copy.
class ContactDraft {
  const ContactDraft({
    required this.card,
    required this.values,
    required this.reviewFields,
    required this.includedEmails,
    required this.includedPhones,
    this.manualEntry = false,
  });

  factory ContactDraft.fromCard(ParsedCard card, {bool manualEntry = false}) {
    return ContactDraft(
      card: card,
      values: {
        for (final field in ContactField.values) field: card[field].value ?? '',
      },
      reviewFields: {
        for (final field in ContactField.values)
          if (card[field].needsReview && card[field].value != null) field,
      },
      // Offered extras are pre-selected: they came off the card.
      includedEmails: card.additionalEmails.toSet(),
      includedPhones: card.additionalPhones.toSet(),
      manualEntry: manualEntry,
    );
  }

  /// What the API returned (kept for confidence display).
  final ParsedCard card;
  final Map<ContactField, String> values;

  /// Fields still highlighted: flagged by the API and not yet touched by the user.
  final Set<ContactField> reviewFields;
  final Set<String> includedEmails;
  final Set<String> includedPhones;

  /// True when the user chose to type the contact because parsing failed.
  final bool manualEntry;

  String operator [](ContactField field) => values[field] ?? '';

  ContactDraft edit(ContactField field, String value) => _copy(
    values: {...values, field: value},
    // Editing a field is the review.
    reviewFields: {...reviewFields}..remove(field),
  );

  ContactDraft includeEmail(String email, {required bool included}) => _copy(
    includedEmails: included
        ? {...includedEmails, email}
        : ({...includedEmails}..remove(email)),
  );

  ContactDraft includePhone(String phone, {required bool included}) => _copy(
    includedPhones: included
        ? {...includedPhones, phone}
        : ({...includedPhones}..remove(phone)),
  );

  Map<ContactField, String> get errors => {
    for (final field in ContactField.values)
      field: ?ContactValidation.validate(field, this[field]),
  };

  bool get hasIdentity => ContactValidation.hasIdentity(values);

  bool get canSave => hasIdentity && errors.isEmpty;

  /// "First Last", falling back to the company; Outlook needs something to show.
  String get displayName {
    final name =
        '${this[ContactField.firstName].trim()} ${this[ContactField.lastName].trim()}'
            .trim();
    return name.isNotEmpty ? name : this[ContactField.companyName].trim();
  }

  ContactDraft _copy({
    Map<ContactField, String>? values,
    Set<ContactField>? reviewFields,
    Set<String>? includedEmails,
    Set<String>? includedPhones,
  }) => ContactDraft(
    card: card,
    values: values ?? this.values,
    reviewFields: reviewFields ?? this.reviewFields,
    includedEmails: includedEmails ?? this.includedEmails,
    includedPhones: includedPhones ?? this.includedPhones,
    manualEntry: manualEntry,
  );
}
