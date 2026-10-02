import 'dart:async';

import 'package:business_card_scanner/app/app.dart';
import 'package:business_card_scanner/core/config/app_config.dart';
import 'package:business_card_scanner/features/authentication/data/auth_service.dart';
import 'package:business_card_scanner/features/contact_preview/data/business_card_api.dart';
import 'package:business_card_scanner/features/contact_preview/data/scan_telemetry_api.dart';
import 'package:business_card_scanner/features/scan_card/data/card_image_picker.dart';
import 'package:business_card_scanner/features/scan_card/data/text_recognition_service.dart';
import 'package:business_card_scanner/features/scan_card/domain/captured_image.dart';
import 'package:business_card_scanner/features/scan_card/domain/ocr_result.dart';
import 'package:business_card_scanner/features/scan_card/presentation/scan_controller.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../support/auth_fakes.dart';
import '../../support/preview_fakes.dart';
import '../../support/scan_fakes.dart';

/// End-to-end UI flow: home → capture → OCR → contact preview, with fakes for
/// the picker, ML Kit and the API. Widget tests use [virtualImage]: real file
/// I/O never completes inside the fake-async zone.
void main() {
  late FakeCardImagePicker picker;
  late FakeTextRecognitionService ocr;
  late FakeBusinessCardApi api;

  setUp(() {
    picker = FakeCardImagePicker();
    ocr = FakeTextRecognitionService();
    api = FakeBusinessCardApi();
  });

  Future<void> pumpSignedInApp(WidgetTester tester) async {
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          appConfigProvider.overrideWithValue(testConfig),
          authServiceProvider.overrideWithValue(
            FakeAuthService()..restoredAccount = testAccount,
          ),
          cardImagePickerProvider.overrideWithValue(picker),
          textRecognitionServiceProvider.overrideWithValue(ocr),
          businessCardApiProvider.overrideWithValue(api),
          scanTelemetryApiProvider.overrideWithValue(FakeScanTelemetryApi()),
        ],
        child: const BusinessCardApp(),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('home offers camera and gallery', (tester) async {
    await pumpSignedInApp(tester);

    expect(find.text('Scan Business Card'), findsOneWidget);
    expect(find.text('Choose from Gallery'), findsOneWidget);
  });

  testWidgets('scan → "Reading card…" → editable contact preview', (
    tester,
  ) async {
    final pending = Completer<OcrResult>();
    ocr.results.add(pending);
    picker.results.add(virtualImage());
    await pumpSignedInApp(tester);

    await tester.tap(find.text('Scan Business Card'));
    await tester.pump();
    await tester.pump();
    expect(find.text('Reading card…'), findsOneWidget);

    pending.complete(ocrResult(['ACME TECHNOLOGY', 'Ahmet Yılmaz']));
    await tester.pumpAndSettle();

    expect(find.text('Review contact'), findsOneWidget);
    expect(find.widgetWithText(TextField, 'Ahmet'), findsOneWidget);
    // Only text and layout hints were sent, in reading order.
    expect(api.requests.single.map((l) => l.text), [
      'ACME TECHNOLOGY',
      'Ahmet Yılmaz',
    ]);
    expect(picker.requestedSources, [CardImageSource.camera]);
  });

  testWidgets('back from the preview returns home', (tester) async {
    picker.results.add(virtualImage());
    await pumpSignedInApp(tester);
    await tester.tap(find.text('Scan Business Card'));
    await tester.pumpAndSettle();
    expect(find.text('Review contact'), findsOneWidget);

    await tester.pageBack();
    await tester.pumpAndSettle();

    expect(find.text('Scan Business Card'), findsOneWidget);
  });

  testWidgets('no text found explains how to retake', (tester) async {
    ocr.results.add(ocrResult([]));
    picker.results.add(virtualImage());
    await pumpSignedInApp(tester);

    await tester.tap(find.text('Scan Business Card'));
    await tester.pumpAndSettle();

    expect(find.text(ScanController.noTextMessage), findsOneWidget);
    expect(find.text('Try again'), findsNothing);
    expect(find.text('Retake photo'), findsOneWidget);
    expect(api.requests, isEmpty);
  });

  testWidgets('a recognizer error offers Try again, then continues', (
    tester,
  ) async {
    ocr.results.add(const OcrFailedException());
    picker.results.add(virtualImage());
    await pumpSignedInApp(tester);

    await tester.tap(find.text('Scan Business Card'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Try again'));
    await tester.pumpAndSettle();

    expect(find.text('Review contact'), findsOneWidget);
  });

  testWidgets('gallery source offers "Choose another photo"', (tester) async {
    ocr.results.add(ocrResult([]));
    picker.results.add(virtualImage(source: CardImageSource.gallery));
    await pumpSignedInApp(tester);

    await tester.tap(find.text('Choose from Gallery'));
    await tester.pumpAndSettle();

    expect(find.text('Choose another photo'), findsOneWidget);
  });

  testWidgets('cancelling the camera stays on home', (tester) async {
    picker.results.add(null);
    await pumpSignedInApp(tester);

    await tester.tap(find.text('Scan Business Card'));
    await tester.pumpAndSettle();

    expect(find.text('Scan Business Card'), findsOneWidget);
    expect(find.byType(SnackBar), findsNothing);
  });

  testWidgets('permission denial shows an explanation', (tester) async {
    picker.results.add(
      const ImageAccessException('Camera access is off. Allow it in Settings.'),
    );
    await pumpSignedInApp(tester);

    await tester.tap(find.text('Scan Business Card'));
    await tester.pumpAndSettle();

    expect(
      find.text('Camera access is off. Allow it in Settings.'),
      findsOneWidget,
    );
  });

  testWidgets('a cancelled retake returns to home', (tester) async {
    ocr.results.add(ocrResult([]));
    picker.results.addAll([virtualImage(), null]);
    await pumpSignedInApp(tester);

    await tester.tap(find.text('Scan Business Card'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Retake photo'));
    await tester.pumpAndSettle();

    expect(find.text('Scan Business Card'), findsOneWidget);
  });
}
