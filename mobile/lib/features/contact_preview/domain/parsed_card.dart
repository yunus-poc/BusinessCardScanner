import 'package:flutter/services.dart';

/// Editable contact fields, in form order. [jsonKey] matches the API contract.
enum ContactField {
  firstName('firstName', 'First name', TextInputType.name),
  lastName('lastName', 'Last name', TextInputType.name),
  companyName('companyName', 'Company', TextInputType.text),
  jobTitle('jobTitle', 'Job title', TextInputType.text),
  department('department', 'Department', TextInputType.text),
  email('email', 'Email', TextInputType.emailAddress),
  mobilePhone('mobilePhone', 'Mobile phone', TextInputType.phone),
  businessPhone('businessPhone', 'Business phone', TextInputType.phone),
  website('website', 'Website', TextInputType.url),
  street('street', 'Street', TextInputType.streetAddress),
  city('city', 'City', TextInputType.text),
  state('state', 'State / region', TextInputType.text),
  postalCode('postalCode', 'Postal code', TextInputType.text),
  country('country', 'Country', TextInputType.text),
  notes('notes', 'Notes', TextInputType.multiline);

  const ContactField(this.jsonKey, this.label, this.keyboardType);

  final String jsonKey;
  final String label;
  final TextInputType keyboardType;
}

/// One field as parsed by the API. [confidence] is a heuristic score, not a
/// probability; [needsReview] is what the UI highlights.
class ParsedField {
  const ParsedField({
    this.value,
    this.confidence = 0,
    this.needsReview = false,
  });

  factory ParsedField.fromJson(Object? json) {
    if (json is! Map<String, dynamic>) {
      return const ParsedField();
    }
    final value = (json['value'] as String?)?.trim();
    return ParsedField(
      value: value == null || value.isEmpty ? null : value,
      confidence: (json['confidence'] as num?)?.toDouble() ?? 0,
      needsReview: json['needsReview'] as bool? ?? false,
    );
  }

  final String? value;
  final double confidence;
  final bool needsReview;
}

/// Why semantic fields may be weaker; drives the "filled without AI" banner.
enum LlmStatus {
  ok,
  timeout,
  unavailable,
  invalidResponse,
  disabled,
  skipped,
  unknown;

  static LlmStatus parse(Object? value) => LlmStatus.values.firstWhere(
    (s) => s.name == value,
    orElse: () => unknown,
  );
}

class ParsedCard {
  const ParsedCard({
    required this.fields,
    this.additionalEmails = const [],
    this.additionalPhones = const [],
    this.llmUsed = false,
    this.fallbackUsed = false,
    this.llmStatus = LlmStatus.unknown,
  });

  factory ParsedCard.fromJson(Map<String, dynamic> json) {
    final contact = json['contact'] as Map<String, dynamic>? ?? const {};
    final meta = json['meta'] as Map<String, dynamic>? ?? const {};
    List<String> strings(Object? list) =>
        (list as List<dynamic>? ?? const []).whereType<String>().toList();

    return ParsedCard(
      fields: {
        for (final field in ContactField.values)
          field: ParsedField.fromJson(contact[field.jsonKey]),
      },
      additionalEmails: strings(json['additionalEmails']),
      additionalPhones: strings(json['additionalPhones']),
      llmUsed: meta['llmUsed'] as bool? ?? false,
      fallbackUsed: meta['fallbackUsed'] as bool? ?? false,
      llmStatus: LlmStatus.parse(meta['llmStatus']),
    );
  }

  /// An empty card for manual entry when parsing is not possible.
  static const empty = ParsedCard(fields: {});

  final Map<ContactField, ParsedField> fields;
  final List<String> additionalEmails;
  final List<String> additionalPhones;
  final bool llmUsed;
  final bool fallbackUsed;
  final LlmStatus llmStatus;

  ParsedField operator [](ContactField field) =>
      fields[field] ?? const ParsedField();
}
