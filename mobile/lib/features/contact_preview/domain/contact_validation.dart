import 'parsed_card.dart';

/// Client-side checks for immediate feedback while editing. The API validates
/// again on save; these rules must stay in line with it.
abstract final class ContactValidation {
  static const maxLength = 256;
  static const maxNotesLength = 2000;

  static final _email = RegExp(
    r'^[A-Za-z0-9._%+\-]+@[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?)*\.[A-Za-z]{2,24}$',
  );
  static final _phone = RegExp(r'^\+?[0-9 ()\-./]{6,24}$');
  static final _controlCharacters = RegExp(r'[\u0000-\u001F\u007F]');

  /// Error message for [value] in [field], or null when valid. Empty is valid:
  /// every field is optional on its own.
  static String? validate(ContactField field, String value) {
    final text = value.trim();
    if (text.isEmpty) {
      return null;
    }
    final limit = field == ContactField.notes ? maxNotesLength : maxLength;
    if (text.length > limit) {
      return 'At most $limit characters.';
    }
    if (field != ContactField.notes && _controlCharacters.hasMatch(text)) {
      return 'Contains invalid characters.';
    }
    return switch (field) {
      ContactField.email when !_email.hasMatch(text) =>
        'Enter a valid email address.',
      ContactField.mobilePhone || ContactField.businessPhone
          when !_isPhone(text) =>
        'Enter a valid phone number.',
      ContactField.website when !_isWebsite(text) =>
        'Enter a valid website, e.g. www.example.com.',
      _ => null,
    };
  }

  /// A contact must identify someone: a name, a company, an email or a phone.
  static bool hasIdentity(Map<ContactField, String> values) => const [
    ContactField.firstName,
    ContactField.lastName,
    ContactField.companyName,
    ContactField.email,
    ContactField.mobilePhone,
    ContactField.businessPhone,
  ].any((f) => (values[f] ?? '').trim().isNotEmpty);

  static bool _isPhone(String text) =>
      _phone.hasMatch(text) && text.replaceAll(RegExp(r'\D'), '').length >= 6;

  static bool _isWebsite(String text) {
    final withScheme = text.contains('://') ? text : 'https://$text';
    final uri = Uri.tryParse(withScheme);
    return uri != null &&
        (uri.scheme == 'http' || uri.scheme == 'https') &&
        uri.host.contains('.') &&
        !text.contains(' ');
  }
}
