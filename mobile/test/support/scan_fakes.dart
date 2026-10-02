import 'dart:async';
import 'dart:io';
import 'dart:ui';

import 'package:business_card_scanner/features/scan_card/data/card_image_picker.dart';
import 'package:business_card_scanner/features/scan_card/data/text_recognition_service.dart';
import 'package:business_card_scanner/features/scan_card/domain/captured_image.dart';
import 'package:business_card_scanner/features/scan_card/domain/ocr_result.dart';

/// Creates a real file so tests can assert it is deleted.
CapturedImage tempImage({
  CardImageSource source = CardImageSource.camera,
  bool isTemporaryCopy = true,
}) {
  final dir = Directory.systemTemp.createTempSync('bc_test_');
  final file = File('${dir.path}/card.jpg')..writeAsBytesSync([0xFF, 0xD8]);
  return CapturedImage(
    path: file.path,
    source: source,
    isTemporaryCopy: isTemporaryCopy,
  );
}

/// A file-less image for widget tests, where real file I/O never completes
/// inside the fake-async zone.
CapturedImage virtualImage({CardImageSource source = CardImageSource.camera}) =>
    CapturedImage(
      path: '/virtual/card.jpg',
      source: source,
      isTemporaryCopy: false,
    );

OcrResult ocrResult(List<String> lines) => OcrResult(
  imageSize: const Size(2048, 1152),
  duration: const Duration(milliseconds: 180),
  lines: [
    for (final (i, text) in lines.indexed)
      OcrLine(
        text: text,
        box: Rect.fromLTWH(100, 100.0 + i * 80, 600, 50),
        blockIndex: i,
        lineIndex: 0,
      ),
  ],
);

class FakeCardImagePicker implements CardImagePicker {
  /// Each pick returns the next result: a [CapturedImage], null (cancelled)
  /// or an [ImageAccessException] to throw.
  final List<Object?> results = [];
  CapturedImage? lostImage;
  final List<CardImageSource> requestedSources = [];

  @override
  Future<CapturedImage?> pick(CardImageSource source) async {
    requestedSources.add(source);
    final result = results.removeAt(0);
    if (result is ImageAccessException) {
      throw result;
    }
    return result as CapturedImage?;
  }

  @override
  Future<CapturedImage?> recoverLostImage() async => lostImage;
}

class FakeTextRecognitionService implements TextRecognitionService {
  /// Each call takes the next entry: an [OcrResult], an exception to throw,
  /// or a [Completer] to control timing. Falls back to [defaultResult].
  final List<Object> results = [];
  OcrResult defaultResult = ocrResult(['ACME TECHNOLOGY', 'Ahmet Yılmaz']);
  final List<CapturedImage> recognized = [];

  @override
  Future<OcrResult> recognize(CapturedImage image) async {
    recognized.add(image);
    final next = results.isEmpty ? defaultResult : results.removeAt(0);
    return switch (next) {
      final OcrResult result => result,
      final Completer<OcrResult> completer => completer.future,
      final Exception error => throw error,
      _ => throw StateError('Unsupported fake result $next'),
    };
  }

  @override
  Future<void> close() async {}
}
