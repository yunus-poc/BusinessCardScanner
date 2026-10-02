import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:business_card_scanner/app/app.dart';
import 'package:business_card_scanner/core/config/app_config.dart';
import 'package:business_card_scanner/core/errors/api_failure.dart';
import 'package:business_card_scanner/features/authentication/data/auth_service.dart';
import 'package:business_card_scanner/features/contact_preview/data/business_card_api.dart';
import 'package:business_card_scanner/features/contact_preview/data/scan_telemetry_api.dart';
import 'package:business_card_scanner/features/contact_preview/domain/contact_draft.dart';
import 'package:business_card_scanner/features/contact_preview/domain/parsed_card.dart';
import 'package:business_card_scanner/features/contact_preview/presentation/contact_preview_controller.dart';
import 'package:business_card_scanner/features/contact_preview/presentation/contact_preview_screen.dart';
import 'package:business_card_scanner/features/contacts/data/contacts_api.dart';
import 'package:business_card_scanner/features/contacts/presentation/save_contact_controller.dart';
import 'package:business_card_scanner/features/scan_card/data/card_image_picker.dart';
import 'package:business_card_scanner/features/scan_card/data/text_recognition_service.dart';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../support/auth_fakes.dart';
import '../../support/preview_fakes.dart';
import '../../support/scan_fakes.dart';

const _existing = DuplicateMatch(
  id: 'existing-1',
  displayName: 'Ahmet Yılmaz',
  companyName: 'ACME',
  email: 'ahmet.yilmaz@acme.com.tr',
  reasons: [DuplicateReason.email, DuplicateReason.mobilePhone],
);

const _other = DuplicateMatch(
  id: 'existing-2',
  displayName: 'A. Yılmaz',
  reasons: [DuplicateReason.nameAndCompany],
);

class FakeContactsApi extends Fake implements ContactsApi {
  /// Results for create/update calls, in order: SavedContact, ApiFailure or Completer.
  final List<Object> results = [];

  /// Returned by the duplicate check (or an ApiFailure to throw).
  Object duplicates = const <DuplicateMatch>[];

  final List<ContactDraft> created = [];
  final List<String> updatedIds = [];

  @override
  Future<List<DuplicateMatch>> checkDuplicates(ContactDraft draft) async =>
      switch (duplicates) {
        final List<DuplicateMatch> matches => matches,
        final Completer<List<DuplicateMatch>> pending => await pending.future,
        final ApiFailure failure => throw failure,
        _ => throw StateError('Unsupported $duplicates'),
      };

  @override
  Future<SavedContact> create(ContactDraft draft) {
    created.add(draft);
    return _next(
      const SavedContact(id: 'graph-1', displayName: 'Ahmet Yılmaz'),
    );
  }

  @override
  Future<SavedContact> update(String contactId, ContactDraft draft) {
    updatedIds.add(contactId);
    return _next(SavedContact(id: contactId, displayName: 'Ahmet Yılmaz'));
  }

  Future<SavedContact> _next(SavedContact fallback) async {
    final next = results.isEmpty ? fallback : results.removeAt(0);
    return switch (next) {
      final SavedContact saved => saved,
      final Completer<SavedContact> completer => completer.future,
      final ApiFailure failure => throw failure,
      _ => throw StateError('Unsupported $next'),
    };
  }
}

class _CapturingAdapter implements HttpClientAdapter {
  RequestOptions? request;
  Object responseBody = {'id': 'graph-1', 'displayName': 'Ahmet Yılmaz'};

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    request = options;
    return ResponseBody.fromString(
      jsonEncode(responseBody),
      options.method == 'POST' && options.path == '/api/contacts' ? 201 : 200,
      headers: {
        Headers.contentTypeHeader: [Headers.jsonContentType],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}

const _rateLimited = ApiFailure(
  code: 'GRAPH_RATE_LIMITED',
  message: 'Outlook is busy. Please try again shortly.',
  retryable: true,
);

const _forbidden = ApiFailure(
  code: 'GRAPH_AUTHORIZATION_FAILED',
  message: 'Your account is not allowed to save Outlook contacts.',
  retryable: false,
);

void main() {
  group('ContactsApi', () {
    test('sends only filled fields and the extras the user kept', () {
      final draft = ContactDraft.fromCard(sampleCard)
          .edit(ContactField.city, '')
          .includeEmail('satis@acme.com.tr', included: true);

      final body = ContactsApi.toSaveRequest(draft);

      expect(body['firstName'], 'Ahmet');
      expect(body.containsKey('city'), isFalse);
      expect(body['additionalEmails'], ['satis@acme.com.tr']);
      expect(body.containsKey('additionalPhones'), isFalse);
    });

    test('deselected extras are not sent', () {
      final draft = ContactDraft.fromCard(sampleCard)
          .includeEmail('satis@acme.com.tr', included: false);

      expect(
        ContactsApi.toSaveRequest(draft).containsKey('additionalEmails'),
        isFalse,
      );
    });

    test(
      'create, update and duplicate check hit the right endpoints',
      () async {
        final adapter = _CapturingAdapter();
        final api = ContactsApi(
          Dio(BaseOptions(baseUrl: 'https://api.test'))
            ..httpClientAdapter = adapter,
        );
        final draft = ContactDraft.fromCard(sampleCard);

        expect((await api.create(draft)).id, 'graph-1');
        expect(
          (adapter.request!.method, adapter.request!.path),
          ('POST', '/api/contacts'),
        );

        await api.update('AAMk/x=', draft);
        expect(
          (adapter.request!.method, adapter.request!.path),
          ('PUT', '/api/contacts/AAMk%2Fx%3D'),
        );

        adapter.responseBody = {
          'possibleDuplicate': true,
          'matches': [
            {
              'id': 'existing-1',
              'displayName': 'Ahmet Yılmaz',
              'companyName': 'ACME',
              'reasons': ['email', 'somethingNew'],
            },
          ],
        };
        final matches = await api.checkDuplicates(draft);
        expect(adapter.request!.path, '/api/contacts/check-duplicate');
        expect(matches.single.id, 'existing-1');
        expect(matches.single.reasons, [
          DuplicateReason.email,
          DuplicateReason.unknown,
        ]);
      },
    );
  });

  group('SaveContactController', () {
    late FakeContactsApi api;
    late ProviderContainer container;

    setUp(() {
      api = FakeContactsApi();
      container = ProviderContainer(
        overrides: [contactsApiProvider.overrideWithValue(api)],
      );
      addTearDown(container.dispose);
    });

    SaveContactController controller() =>
        container.read(saveContactControllerProvider.notifier);
    SaveState state() => container.read(saveContactControllerProvider);
    final draft = ContactDraft.fromCard(sampleCard);

    test('no duplicate: checks, then creates', () async {
      await controller().save(draft);

      expect(state(), isA<Saved>());
      expect(api.created, hasLength(1));
    });

    test('a duplicate waits for the user instead of creating', () async {
      api.duplicates = [_existing];

      await controller().save(draft);

      expect((state() as DuplicateFound).matches.single.id, 'existing-1');
      expect(api.created, isEmpty);
    });

    test('update existing writes to the chosen contact', () async {
      api.duplicates = [_existing, _other];
      await controller().save(draft);

      await controller().updateExisting(_other);

      final saved = state() as Saved;
      expect(saved.updatedExisting, isTrue);
      expect(api.updatedIds, ['existing-2']);
      expect(api.created, isEmpty);
    });

    test('create anyway creates a new contact', () async {
      api.duplicates = [_existing];
      await controller().save(draft);

      await controller().createAnyway();

      expect((state() as Saved).updatedExisting, isFalse);
      expect(api.created, hasLength(1));
    });

    test('cancel returns to the form with nothing saved', () async {
      api.duplicates = [_existing];
      await controller().save(draft);

      controller().cancel();

      expect(state(), isA<SaveIdle>());
      expect(api.created, isEmpty);
      expect(api.updatedIds, isEmpty);
    });

    test('a second tap while busy cannot create the contact twice', () async {
      final pending = Completer<SavedContact>();
      api.results.add(pending);

      final first = controller().save(draft);
      await Future<void>.delayed(Duration.zero);
      await controller().save(draft);
      pending.complete(const SavedContact(id: 'g', displayName: 'x'));
      await first;

      expect(api.created, hasLength(1));
    });

    test(
      'a failed duplicate check is reported and nothing is created',
      () async {
        api.duplicates = _rateLimited;

        await controller().save(draft);

        expect((state() as SaveFailed).failure.retryable, isTrue);
        expect(api.created, isEmpty);
      },
    );
  });

  group('save flow UI', () {
    late FakeContactsApi contacts;

    setUp(() => contacts = FakeContactsApi());

    Future<void> pumpApp(WidgetTester tester) async {
      final picker = FakeCardImagePicker()..results.add(virtualImage());
      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            appConfigProvider.overrideWithValue(testConfig),
            authServiceProvider.overrideWithValue(
              FakeAuthService()..restoredAccount = testAccount,
            ),
            cardImagePickerProvider.overrideWithValue(picker),
            textRecognitionServiceProvider.overrideWithValue(
              FakeTextRecognitionService(),
            ),
            businessCardApiProvider.overrideWithValue(FakeBusinessCardApi()),
            scanTelemetryApiProvider.overrideWithValue(FakeScanTelemetryApi()),
            contactsApiProvider.overrideWithValue(contacts),
          ],
          child: const BusinessCardApp(),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('Scan Business Card'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Save to Outlook'));
      await tester.pumpAndSettle();
    }

    testWidgets('no duplicate: straight to "Contact saved to Outlook."', (
      tester,
    ) async {
      await pumpApp(tester);

      expect(find.text(SaveContactController.successMessage), findsOneWidget);
      expect(contacts.created.single[ContactField.firstName], 'Ahmet');

      await tester.tap(find.text('Scan another card'));
      await tester.pumpAndSettle();
      expect(find.text('Scan Business Card'), findsOneWidget);
    });

    testWidgets('duplicate → dialog with reasons → update existing', (
      tester,
    ) async {
      contacts.duplicates = [_existing];
      await pumpApp(tester);

      expect(find.text(SaveContactController.duplicateTitle), findsOneWidget);
      expect(find.text('Same email · Same mobile number'), findsOneWidget);
      await tester.tap(find.text('Update existing'));
      await tester.pumpAndSettle();

      expect(find.text(SaveContactController.successMessage), findsOneWidget);
      expect(find.text('The existing contact was updated.'), findsOneWidget);
      expect(contacts.updatedIds, ['existing-1']);
    });

    testWidgets('duplicate → create anyway', (tester) async {
      contacts.duplicates = [_existing];
      await pumpApp(tester);

      await tester.tap(find.text('Create anyway'));
      await tester.pumpAndSettle();

      expect(find.text(SaveContactController.successMessage), findsOneWidget);
      expect(contacts.created, hasLength(1));
    });

    testWidgets('duplicate → cancel keeps the form', (tester) async {
      contacts.duplicates = [_existing];
      await pumpApp(tester);

      await tester.tap(find.text('Cancel'));
      await tester.pumpAndSettle();

      expect(find.text('Review contact'), findsOneWidget);
      expect(find.text('Save to Outlook'), findsOneWidget);
      expect(contacts.created, isEmpty);
    });

    testWidgets('several duplicates: the selected one is updated', (
      tester,
    ) async {
      contacts.duplicates = [_existing, _other];
      await pumpApp(tester);

      await tester.tap(find.text('A. Yılmaz'));
      await tester.pump();
      await tester.tap(find.text('Update existing'));
      await tester.pumpAndSettle();

      expect(contacts.updatedIds, ['existing-2']);
    });

    Future<void> pumpPreview(WidgetTester tester) async {
      final container = ProviderContainer(
        overrides: [
          businessCardApiProvider.overrideWithValue(FakeBusinessCardApi()),
          scanTelemetryApiProvider.overrideWithValue(FakeScanTelemetryApi()),
          contactsApiProvider.overrideWithValue(contacts),
        ],
      );
      addTearDown(container.dispose);
      await tester.pumpWidget(
        UncontrolledProviderScope(
          container: container,
          child: const MaterialApp(home: ContactPreviewScreen()),
        ),
      );
      unawaited(
        container
            .read(contactPreviewControllerProvider.notifier)
            .start(const []),
      );
      await tester.pumpAndSettle();
    }

    testWidgets('retryable failure turns the button into Try again', (
      tester,
    ) async {
      contacts.results.add(_rateLimited);
      await pumpPreview(tester);

      await tester.tap(find.text('Save to Outlook'));
      await tester.pumpAndSettle();

      expect(
        find.text('Outlook is busy. Please try again shortly.'),
        findsOneWidget,
      );
      expect(find.text('Try again'), findsOneWidget);
    });

    testWidgets('non-retryable failure explains and does not offer a retry', (
      tester,
    ) async {
      contacts.results.add(_forbidden);
      await pumpPreview(tester);

      await tester.tap(find.text('Save to Outlook'));
      await tester.pumpAndSettle();

      expect(
        find.text('Your account is not allowed to save Outlook contacts.'),
        findsOneWidget,
      );
      expect(find.text('Try again'), findsNothing);
    });

    testWidgets(
      'progress labels follow the save steps and the button is disabled',
      (tester) async {
        final check = Completer<List<DuplicateMatch>>();
        contacts.duplicates = check;
        contacts.results.add(Completer<SavedContact>());
        await pumpPreview(tester);
        FilledButton button() =>
            tester.widget<FilledButton>(find.byType(FilledButton));

        await tester.tap(find.text('Save to Outlook'));
        await tester.pump();
        expect(find.text('Checking for duplicates…'), findsOneWidget);
        expect(button().onPressed, isNull);

        check.complete(const []);
        await tester.pump();
        expect(find.text('Saving…'), findsOneWidget);
        expect(button().onPressed, isNull);
        // The create is left pending: completing would navigate, and this standalone screen has no router.
      },
    );
  });
}
