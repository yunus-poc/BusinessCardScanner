import 'dart:convert';
import 'dart:typed_data';

import 'package:business_card_scanner/core/errors/api_failure.dart';
import 'package:business_card_scanner/features/contact_preview/data/business_card_api.dart';
import 'package:business_card_scanner/features/contact_preview/domain/contact_draft.dart';
import 'package:business_card_scanner/features/contact_preview/domain/contact_validation.dart';
import 'package:business_card_scanner/features/contact_preview/domain/parsed_card.dart';
import 'package:business_card_scanner/features/scan_card/domain/layout_line.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../support/preview_fakes.dart';

class _RecordingAdapter implements HttpClientAdapter {
  _RecordingAdapter(this.status, this.body);

  final int status;
  final Object body;
  RequestOptions? lastRequest;
  Object? lastBody;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    lastRequest = options;
    lastBody = options.data;
    return ResponseBody.fromString(
      jsonEncode(body),
      status,
      headers: {
        Headers.contentTypeHeader: [Headers.jsonContentType],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}

void main() {
  group('ParsedCard.fromJson', () {
    test('maps every field, extras and meta', () {
      final card = sampleCard;

      expect(card[ContactField.firstName].value, 'Ahmet');
      expect(card[ContactField.jobTitle].needsReview, isTrue);
      expect(card[ContactField.jobTitle].confidence, 0.72);
      expect(card[ContactField.street].value, isNull);
      expect(card.additionalEmails, ['satis@acme.com.tr']);
      expect(card.llmStatus, LlmStatus.ok);
      expect(card.fallbackUsed, isFalse);
    });

    test('tolerates nulls, blanks, missing sections and unknown statuses', () {
      final card = ParsedCard.fromJson({
        'contact': {
          'firstName': {'value': null, 'confidence': 0, 'needsReview': false},
          'lastName': {'value': '   '},
          'email': 'not-an-object',
        },
        'meta': {'llmStatus': 'somethingNew'},
      });

      expect(card[ContactField.firstName].value, isNull);
      expect(card[ContactField.lastName].value, isNull);
      expect(card[ContactField.email].value, isNull);
      expect(card.additionalPhones, isEmpty);
      expect(card.llmStatus, LlmStatus.unknown);
    });
  });

  group('BusinessCardApi', () {
    const line = LayoutLine(
      text: 'Ahmet Yılmaz',
      order: 1,
      region: LayoutRegion.center,
      size: LayoutSize.large,
      align: LayoutAlign.center,
      sameBlock: false,
      gap: LayoutGap.none,
    );

    test('posts layout lines and maps the response', () async {
      final adapter = _RecordingAdapter(
        200,
        parseResponse(fields: {'firstName': ('Ahmet', 0.97, false)}),
      );
      final dio = Dio(BaseOptions(baseUrl: 'https://api.test'))
        ..httpClientAdapter = adapter;

      final card = await BusinessCardApi(dio).parse([line]);

      expect(adapter.lastRequest!.path, '/api/business-cards/parse');
      expect(adapter.lastBody, {
        'lines': [line.toJson()],
      });
      expect(card[ContactField.firstName].value, 'Ahmet');
    });

    test('maps ProblemDetails failures to ApiFailure', () async {
      final adapter = _RecordingAdapter(400, {
        'code': 'VALIDATION_FAILED',
        'detail': 'Bad lines',
        'retryable': false,
      });
      final dio = Dio(BaseOptions(baseUrl: 'https://api.test'))
        ..httpClientAdapter = adapter;

      await expectLater(
        BusinessCardApi(dio).parse([line]),
        throwsA(
          isA<ApiFailure>().having((f) => f.code, 'code', 'VALIDATION_FAILED'),
        ),
      );
    });
  });

  group('ContactValidation', () {
    test('accepts empty and well-formed values', () {
      expect(ContactValidation.validate(ContactField.email, ''), isNull);
      expect(
        ContactValidation.validate(ContactField.email, 'a.b@acme.com.tr'),
        isNull,
      );
      expect(
        ContactValidation.validate(
          ContactField.mobilePhone,
          '+90 532 123 45 67',
        ),
        isNull,
      );
      expect(
        ContactValidation.validate(ContactField.website, 'www.acme.com.tr'),
        isNull,
      );
      expect(
        ContactValidation.validate(ContactField.website, 'https://acme.io/tr'),
        isNull,
      );
    });

    test('rejects malformed values', () {
      expect(
        ContactValidation.validate(ContactField.email, 'ahmet@acme'),
        isNotNull,
      );
      expect(
        ContactValidation.validate(ContactField.mobilePhone, '12'),
        isNotNull,
      );
      expect(
        ContactValidation.validate(ContactField.mobilePhone, 'call me'),
        isNotNull,
      );
      expect(
        ContactValidation.validate(ContactField.website, 'not a site'),
        isNotNull,
      );
      expect(
        ContactValidation.validate(ContactField.firstName, 'a' * 300),
        isNotNull,
      );
      expect(
        ContactValidation.validate(ContactField.companyName, 'ACME\u0007'),
        isNotNull,
      );
    });

    test('a contact needs at least one identifying field', () {
      expect(
        ContactValidation.hasIdentity({ContactField.city: 'İstanbul'}),
        isFalse,
      );
      expect(
        ContactValidation.hasIdentity({ContactField.companyName: 'NOVA'}),
        isTrue,
      );
      expect(
        ContactValidation.hasIdentity({
          ContactField.mobilePhone: '+90 532 123 45 67',
        }),
        isTrue,
      );
    });
  });

  group('ContactDraft', () {
    test('starts from the parse with flagged fields highlighted', () {
      final draft = ContactDraft.fromCard(sampleCard);

      expect(draft[ContactField.companyName], 'ACME Technology');
      expect(draft.reviewFields, {ContactField.jobTitle, ContactField.city});
      expect(draft.includedEmails, {'satis@acme.com.tr'});
      expect(draft.displayName, 'Ahmet Yılmaz');
      expect(draft.canSave, isTrue);
    });

    test('editing a field clears its highlight and keeps others', () {
      final draft = ContactDraft.fromCard(sampleCard)
          .edit(ContactField.jobTitle, 'Solutions Manager');

      expect(draft[ContactField.jobTitle], 'Solutions Manager');
      expect(draft.reviewFields, {ContactField.city});
    });

    test('an invalid edit blocks saving', () {
      final draft = ContactDraft.fromCard(sampleCard)
          .edit(ContactField.email, 'ahmet@');

      expect(draft.errors.keys, [ContactField.email]);
      expect(draft.canSave, isFalse);
    });

    test('extras can be deselected', () {
      final draft = ContactDraft.fromCard(sampleCard)
          .includeEmail('satis@acme.com.tr', included: false);

      expect(draft.includedEmails, isEmpty);
    });

    test('display name falls back to the company', () {
      final draft = ContactDraft.fromCard(ParsedCard.empty)
          .edit(ContactField.companyName, 'NOVA');

      expect(draft.displayName, 'NOVA');
    });

    test('manual entry starts empty and cannot be saved until something identifies the contact', () {
      final draft = ContactDraft.fromCard(ParsedCard.empty, manualEntry: true);

      expect(draft.manualEntry, isTrue);
      expect(draft.reviewFields, isEmpty);
      expect(draft.canSave, isFalse);
      expect(draft.edit(ContactField.lastName, 'Kaya').canSave, isTrue);
    });
  });
}
