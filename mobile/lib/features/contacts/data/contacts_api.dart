import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../app/api_provider.dart';
import '../../../core/errors/api_failure.dart';
import '../../contact_preview/domain/contact_draft.dart';
import '../../contact_preview/domain/parsed_card.dart';

class SavedContact {
  const SavedContact({required this.id, required this.displayName});

  factory SavedContact.fromJson(Map<String, dynamic> json) => SavedContact(
    id: json['id'] as String? ?? '',
    displayName: json['displayName'] as String? ?? '',
  );

  /// Opaque Outlook contact id.
  final String id;
  final String displayName;
}

enum DuplicateReason {
  email,
  mobilePhone,
  nameAndCompany,
  unknown;

  static DuplicateReason parse(Object? value) => DuplicateReason.values
      .firstWhere((r) => r.name == value, orElse: () => unknown);

  String get label => switch (this) {
    email => 'Same email',
    mobilePhone => 'Same mobile number',
    nameAndCompany => 'Same name and company',
    unknown => 'Similar contact',
  };
}

/// An existing Outlook contact that may be the person on the card.
class DuplicateMatch {
  const DuplicateMatch({
    required this.id,
    required this.displayName,
    this.companyName,
    this.email,
    this.mobilePhone,
    this.reasons = const [],
  });

  factory DuplicateMatch.fromJson(Map<String, dynamic> json) => DuplicateMatch(
    id: json['id'] as String? ?? '',
    displayName: json['displayName'] as String? ?? '',
    companyName: json['companyName'] as String?,
    email: json['email'] as String?,
    mobilePhone: json['mobilePhone'] as String?,
    reasons: [
      for (final reason in json['reasons'] as List<dynamic>? ?? const [])
        DuplicateReason.parse(reason),
    ],
  );

  final String id;
  final String displayName;
  final String? companyName;
  final String? email;
  final String? mobilePhone;
  final List<DuplicateReason> reasons;
}

/// Writes confirmed contacts to the user's Outlook through BusinessCard.Api.
/// The app never talks to Microsoft Graph directly.
class ContactsApi {
  ContactsApi(this._dio);

  final Dio _dio;

  /// Throws [ApiFailure].
  Future<SavedContact> create(ContactDraft draft) => _send(
    () => _dio.post<Map<String, dynamic>>(
      '/api/contacts',
      data: toSaveRequest(draft),
    ),
  );

  /// Existing contacts that may be this person. Empty when there are none.
  Future<List<DuplicateMatch>> checkDuplicates(ContactDraft draft) async {
    try {
      final response = await _dio.post<Map<String, dynamic>>(
        '/api/contacts/check-duplicate',
        data: toSaveRequest(draft),
      );
      return [
        for (final match
            in response.data?['matches'] as List<dynamic>? ?? const [])
          DuplicateMatch.fromJson(match as Map<String, dynamic>),
      ];
    } on DioException catch (e) {
      throw ApiFailure.fromDioException(e);
    }
  }

  /// Updates an existing contact (explicit choice after a duplicate warning).
  Future<SavedContact> update(String contactId, ContactDraft draft) => _send(
    () => _dio.put<Map<String, dynamic>>(
      '/api/contacts/${Uri.encodeComponent(contactId)}',
      data: toSaveRequest(draft),
    ),
  );

  /// Only non-empty fields are sent; extras only when the user kept them.
  static Map<String, Object> toSaveRequest(ContactDraft draft) => {
    for (final field in ContactField.values)
      if (draft[field].trim().isNotEmpty) field.jsonKey: draft[field].trim(),
    if (draft.includedEmails.isNotEmpty)
      'additionalEmails': [
        for (final email in draft.card.additionalEmails)
          if (draft.includedEmails.contains(email)) email,
      ],
    if (draft.includedPhones.isNotEmpty)
      'additionalPhones': [
        for (final phone in draft.card.additionalPhones)
          if (draft.includedPhones.contains(phone)) phone,
      ],
  };

  Future<SavedContact> _send(
    Future<Response<Map<String, dynamic>>> Function() request,
  ) async {
    try {
      final response = await request();
      return SavedContact.fromJson(response.data ?? const {});
    } on DioException catch (e) {
      throw ApiFailure.fromDioException(e);
    }
  }
}

final contactsApiProvider = Provider<ContactsApi>(
  (ref) => ContactsApi(ref.watch(apiClientProvider)),
);
