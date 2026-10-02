import 'dart:async';

import 'package:business_card_scanner/features/contact_preview/data/business_card_api.dart';
import 'package:business_card_scanner/features/contact_preview/data/scan_telemetry_api.dart';
import 'package:business_card_scanner/features/contact_preview/domain/parsed_card.dart';
import 'package:business_card_scanner/features/contact_preview/presentation/contact_preview_controller.dart';
import 'package:business_card_scanner/features/contact_preview/presentation/contact_preview_screen.dart';
import 'package:business_card_scanner/features/scan_card/domain/layout_line.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../support/preview_fakes.dart';

const _layout = [
  LayoutLine(
    text: 'Ahmet Yılmaz',
    order: 1,
    region: LayoutRegion.center,
    size: LayoutSize.large,
    align: LayoutAlign.center,
    sameBlock: false,
    gap: LayoutGap.none,
  ),
];

void main() {
  late FakeBusinessCardApi api;

  setUp(() => api = FakeBusinessCardApi());

  group('ContactPreviewController', () {
    late ProviderContainer container;

    setUp(() {
      container = ProviderContainer(
        overrides: [businessCardApiProvider.overrideWithValue(api)],
      );
      addTearDown(container.dispose);
    });

    ContactPreviewController controller() =>
        container.read(contactPreviewControllerProvider.notifier);
    PreviewState state() => container.read(contactPreviewControllerProvider);

    test('loading → ready with an editable draft and a timing', () async {
      final pending = Completer<ParsedCard>();
      api.results.add(pending);

      final future = controller().start(_layout);
      expect(state(), isA<PreviewLoading>());
      pending.complete(sampleCard);
      await future;

      final ready = state() as PreviewReady;
      expect(ready.draft[ContactField.firstName], 'Ahmet');
      expect(ready.ocrToPreview, isNotNull);
    });

    test('a failure keeps the layout so retry needs no rescan', () async {
      api.results.addAll([networkFailure, sampleCard]);

      await controller().start(_layout);
      expect((state() as PreviewFailed).failure.retryable, isTrue);

      await controller().retry();
      expect(state(), isA<PreviewReady>());
      expect(api.requests, hasLength(2));
      expect(api.requests.last.single.text, 'Ahmet Yılmaz');
    });

    test('manual entry is always possible after a failure', () async {
      api.results.add(validationFailure);
      await controller().start(_layout);

      controller().enterManually();

      expect((state() as PreviewReady).draft.manualEntry, isTrue);
    });

    test('edits update the draft', () async {
      await controller().start(_layout);

      controller().updateField(ContactField.jobTitle, 'CTO');

      final draft = (state() as PreviewReady).draft;
      expect(draft[ContactField.jobTitle], 'CTO');
      expect(draft.reviewFields.contains(ContactField.jobTitle), isFalse);
    });

    test('reports OCR and OCR-to-preview timing once per scan', () async {
      final telemetry = FakeScanTelemetryApi();
      final scoped = ProviderContainer(
        overrides: [
          businessCardApiProvider.overrideWithValue(api),
          scanTelemetryApiProvider.overrideWithValue(telemetry),
        ],
      );
      addTearDown(scoped.dispose);
      final preview = scoped.read(contactPreviewControllerProvider.notifier);
      api.results.addAll([networkFailure, sampleCard, sampleCard]);

      await preview.start(
        _layout,
        ocrDuration: const Duration(milliseconds: 420),
      );
      expect(
        telemetry.reports,
        isEmpty,
        reason: 'a failed parse is not a preview',
      );

      await preview.retry();
      final (ocr, toPreview, status) = telemetry.reports.single;
      expect(ocr, const Duration(milliseconds: 420));
      expect(toPreview, greaterThan(Duration.zero));
      expect(status, LlmStatus.ok);

      await preview.retry();
      expect(telemetry.reports, hasLength(1), reason: 'reported once per scan');
    });

    test('a failing telemetry call never affects the preview', () async {
      final scoped = ProviderContainer(
        overrides: [
          businessCardApiProvider.overrideWithValue(api),
          scanTelemetryApiProvider.overrideWithValue(_ThrowingTelemetry()),
        ],
      );
      addTearDown(scoped.dispose);

      await scoped
          .read(contactPreviewControllerProvider.notifier)
          .start(_layout, ocrDuration: const Duration(milliseconds: 1));

      expect(
        scoped.read(contactPreviewControllerProvider),
        isA<PreviewReady>(),
      );
    });

    test('reset forgets everything and ignores a late response', () async {
      final pending = Completer<ParsedCard>();
      api.results.add(pending);
      final future = controller().start(_layout);

      controller().reset();
      pending.complete(sampleCard);
      await future;

      expect(state(), isA<PreviewIdle>());
    });
  });

  group('ContactPreviewScreen', () {
    Future<ProviderContainer> pumpPreview(WidgetTester tester) async {
      // Tall surface: ListView builds lazily, and these tests inspect every field.
      tester.view.physicalSize = const Size(1080, 4000);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);

      final container = ProviderContainer(
        overrides: [businessCardApiProvider.overrideWithValue(api)],
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
            .start(_layout),
      );
      await tester.pumpAndSettle();
      return container;
    }

    testWidgets('shows parsed values and highlights fields needing review', (
      tester,
    ) async {
      await pumpPreview(tester);

      expect(find.widgetWithText(TextField, 'Ahmet'), findsOneWidget);
      expect(find.widgetWithText(TextField, 'ACME Technology'), findsOneWidget);
      expect(find.text('Please check'), findsNWidgets(2)); // job title, city
      expect(find.text('2 fields need a quick check.'), findsOneWidget);
    });

    testWidgets('editing a highlighted field removes its highlight', (
      tester,
    ) async {
      await pumpPreview(tester);

      await tester.enterText(
        find.widgetWithText(TextField, 'Enterprise Solutions Manager'),
        'Solutions Manager',
      );
      await tester.pump();

      expect(find.text('Please check'), findsOneWidget);
      expect(find.text('1 field needs a quick check.'), findsOneWidget);
    });

    testWidgets('fallback results explain why fields are highlighted', (
      tester,
    ) async {
      api.defaultCard = ParsedCard.fromJson(
        parseResponse(
          fields: {'firstName': ('Ahmet', 0.6, true)},
          fallbackUsed: true,
          llmStatus: 'timeout',
        ),
      );

      await pumpPreview(tester);

      expect(find.text(ContactPreviewScreen.fallbackBanner), findsOneWidget);
    });

    testWidgets('recoverable failure offers retry and manual entry', (
      tester,
    ) async {
      api.results.add(networkFailure);

      await pumpPreview(tester);

      expect(find.text('No connection to the server.'), findsOneWidget);
      expect(find.text('Reference: trace-123'), findsOneWidget);
      await tester.tap(find.text('Try again'));
      await tester.pumpAndSettle();
      expect(find.widgetWithText(TextField, 'Ahmet'), findsOneWidget);
    });

    testWidgets('non-recoverable failure offers manual entry only', (
      tester,
    ) async {
      api.results.add(validationFailure);

      await pumpPreview(tester);

      expect(find.text('Try again'), findsNothing);
      await tester.tap(find.text('Enter manually'));
      await tester.pumpAndSettle();
      expect(find.text('Enter the contact details.'), findsOneWidget);
    });

    testWidgets('invalid input shows an error and disables saving', (
      tester,
    ) async {
      await pumpPreview(tester);

      await tester.enterText(
        find.widgetWithText(TextField, 'ahmet.yilmaz@acme.com.tr'),
        'ahmet@',
      );
      await tester.pump();

      expect(find.text('Enter a valid email address.'), findsOneWidget);
      final save = tester.widget<FilledButton>(
        find.widgetWithText(FilledButton, 'Save to Outlook'),
      );
      expect(save.onPressed, isNull);
    });

    testWidgets('extras are offered and can be deselected', (tester) async {
      final container = await pumpPreview(tester);

      await tester.tap(find.text('satis@acme.com.tr'));
      await tester.pump();

      final draft = (container.read(
        contactPreviewControllerProvider,
      ) as PreviewReady).draft;
      expect(draft.includedEmails, isEmpty);
    });
  });
}

class _ThrowingTelemetry extends Fake implements ScanTelemetryApi {
  @override
  Future<void> reportScan({
    required Duration ocr,
    required Duration ocrToPreview,
    required LlmStatus llmStatus,
  }) => Future.error(StateError('telemetry endpoint down'));
}
