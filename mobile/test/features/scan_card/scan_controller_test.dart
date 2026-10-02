import 'dart:async';
import 'dart:io';

import 'package:business_card_scanner/features/scan_card/data/card_image_picker.dart';
import 'package:business_card_scanner/features/scan_card/data/text_recognition_service.dart';
import 'package:business_card_scanner/features/scan_card/domain/captured_image.dart';
import 'package:business_card_scanner/features/scan_card/domain/ocr_result.dart';
import 'package:business_card_scanner/features/scan_card/domain/scan_state.dart';
import 'package:business_card_scanner/features/scan_card/presentation/scan_controller.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../support/scan_fakes.dart';

void main() {
  late FakeCardImagePicker picker;
  late FakeTextRecognitionService ocr;
  late ProviderContainer container;

  setUp(() {
    picker = FakeCardImagePicker();
    ocr = FakeTextRecognitionService();
    container = ProviderContainer(
      overrides: [
        cardImagePickerProvider.overrideWithValue(picker),
        textRecognitionServiceProvider.overrideWithValue(ocr),
      ],
    );
  });

  tearDown(() => container.dispose());

  ScanController controller() =>
      container.read(scanControllerProvider.notifier);
  ScanState state() => container.read(scanControllerProvider);

  /// Deletion runs in the background after OCR; wait for it, bounded.
  Future<bool> eventuallyDeleted(CapturedImage image) async {
    for (var i = 0; i < 50; i++) {
      if (!File(image.path).existsSync()) {
        return true;
      }
      await Future<void>.delayed(const Duration(milliseconds: 10));
    }
    return false;
  }

  Future<void> settle() => Future<void>.delayed(Duration.zero);

  group('capture → OCR', () {
    test('a captured image is recognized, then deleted', () async {
      final image = tempImage();
      picker.results.add(image);

      expect(await controller().capture(CardImageSource.camera), isTrue);
      await settle();

      final recognized = state() as ScanRecognized;
      expect(
        recognized.result.lines.map((l) => l.text),
        contains('Ahmet Yılmaz'),
      );
      expect(ocr.recognized.single, same(image));
      expect(await eventuallyDeleted(image), isTrue);
    });

    test('shows Recognizing while OCR runs', () async {
      final pending = Completer<OcrResult>();
      ocr.results.add(pending);
      picker.results.add(tempImage());

      await controller().capture(CardImageSource.camera);
      expect(state(), isA<ScanRecognizing>());

      pending.complete(ocrResult(['NOVA']));
      await settle();
      expect(state(), isA<ScanRecognized>());
    });

    test('no text found asks for a new photo and deletes the image', () async {
      final image = tempImage();
      ocr.results.add(ocrResult([]));
      picker.results.add(image);

      await controller().capture(CardImageSource.camera);
      await settle();

      final failed = state() as ScanOcrFailed;
      expect(failed.message, ScanController.noTextMessage);
      expect(failed.canRetry, isFalse);
      expect(await eventuallyDeleted(image), isTrue);
    });

    test('a recognizer error keeps the image for one-tap retry', () async {
      final image = tempImage();
      ocr.results.add(const OcrFailedException());
      picker.results.add(image);

      await controller().capture(CardImageSource.camera);
      await settle();
      final failed = state() as ScanOcrFailed;
      expect(failed.canRetry, isTrue);
      expect(File(image.path).existsSync(), isTrue);

      controller().retryRecognition();
      await settle();

      expect(state(), isA<ScanRecognized>());
      expect(ocr.recognized, [same(image), same(image)]);
      expect(await eventuallyDeleted(image), isTrue);
    });

    test(
      'a late result from an abandoned scan never replaces a newer one',
      () async {
        final slow = Completer<OcrResult>();
        ocr.results.addAll([
          slow,
          ocrResult(['SECOND']),
        ]);
        final first = tempImage();
        picker.results.addAll([first, tempImage()]);

        // Leave while the first OCR is still running, then scan again.
        await controller().capture(CardImageSource.camera);
        await controller().reset();
        await controller().capture(CardImageSource.camera);
        await settle();
        slow.complete(ocrResult(['FIRST']));
        await settle();

        final recognized = state() as ScanRecognized;
        expect(recognized.result.lines.single.text, 'SECOND');
        expect(File(first.path).existsSync(), isFalse);
      },
    );

    test('capture is ignored while OCR is running', () async {
      ocr.results.add(Completer<OcrResult>());
      picker.results.add(tempImage());

      await controller().capture(CardImageSource.camera);

      expect(await controller().capture(CardImageSource.camera), isFalse);
      expect(picker.requestedSources, hasLength(1));
    });
  });

  group('picking', () {
    test('cancelling returns to idle without running OCR', () async {
      picker.results.add(null);

      expect(await controller().capture(CardImageSource.gallery), isFalse);
      expect(state(), isA<ScanIdle>());
      expect(ocr.recognized, isEmpty);
    });

    test('access errors are shown once, then cleared', () async {
      picker.results.add(const ImageAccessException('Camera access is off.'));

      await controller().capture(CardImageSource.camera);
      expect((state() as ScanFailed).message, 'Camera access is off.');

      controller().acknowledgeError();
      expect(state(), isA<ScanIdle>());
    });

    test('recovers an image lost while Android killed the app', () async {
      picker.lostImage = tempImage();

      expect(await controller().recoverLostImage(), isTrue);
      await settle();
      expect(state(), isA<ScanRecognized>());
    });
  });

  group('cleanup', () {
    test(
      'reset during OCR deletes the image and ignores the late result',
      () async {
        final pending = Completer<OcrResult>();
        ocr.results.add(pending);
        final image = tempImage();
        picker.results.add(image);
        await controller().capture(CardImageSource.camera);

        await controller().reset();
        pending.complete(ocrResult(['LATE']));
        await settle();

        expect(state(), isA<ScanIdle>());
        expect(File(image.path).existsSync(), isFalse);
      },
    );

    test('reset keeps a pending access error for the home screen', () async {
      picker.results.add(const ImageAccessException('No camera.'));
      await controller().capture(CardImageSource.camera);

      await controller().reset();

      expect(state(), isA<ScanFailed>());
    });

    test("never deletes a file that is not the app's temporary copy", () async {
      final original = tempImage(isTemporaryCopy: false);
      picker.results.add(original);

      await controller().capture(CardImageSource.gallery);
      await settle();
      await controller().reset();

      expect(File(original.path).existsSync(), isTrue);
    });

    test('disposing the controller deletes a retained image', () async {
      final image = tempImage();
      ocr.results.add(const OcrFailedException());
      picker.results.add(image);
      await controller().capture(CardImageSource.camera);
      await settle();

      container.dispose();

      expect(await eventuallyDeleted(image), isTrue);
    });
  });
}
