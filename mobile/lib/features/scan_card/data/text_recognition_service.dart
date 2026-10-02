import 'dart:math' as math;
import 'dart:ui' as ui;

import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:google_mlkit_text_recognition/google_mlkit_text_recognition.dart';

import '../domain/captured_image.dart';
import '../domain/ocr_result.dart';

class OcrFailedException implements Exception {
  const OcrFailedException();
}

/// On-device text recognition with Google ML Kit (Latin script, which covers
/// Turkish: Ç Ğ İ Ö Ş Ü). The image never leaves the device.
///
/// One recognizer lives for the app's lifetime so the model stays loaded:
/// creating it per scan would add model start-up time to every scan.
class TextRecognitionService {
  TextRecognitionService({
    TextRecognizer? recognizer,
    Future<ui.Size> Function(String path)? readImageSize,
  }) : _recognizer =
           recognizer ?? TextRecognizer(script: TextRecognitionScript.latin),
       _readImageSize = readImageSize ?? _decodeImageSize;

  final TextRecognizer _recognizer;
  final Future<ui.Size> Function(String path) _readImageSize;

  Future<OcrResult> recognize(CapturedImage image) async {
    final stopwatch = Stopwatch()..start();
    try {
      final (recognized, size) = await (
        _recognizer.processImage(InputImage.fromFilePath(image.path)),
        _readImageSize(image.path),
      ).wait;
      stopwatch.stop();
      return mapRecognizedText(recognized, size, stopwatch.elapsed);
    } on PlatformException {
      throw const OcrFailedException();
    } on ParallelWaitError<dynamic, dynamic> {
      throw const OcrFailedException();
    }
  }

  Future<void> close() => _recognizer.close();

  /// Reads only the image header, without decoding pixels.
  static Future<ui.Size> _decodeImageSize(String path) async {
    final buffer = await ui.ImmutableBuffer.fromFilePath(path);
    final descriptor = await ui.ImageDescriptor.encoded(buffer);
    try {
      return ui.Size(descriptor.width.toDouble(), descriptor.height.toDouble());
    } finally {
      descriptor.dispose();
      buffer.dispose();
    }
  }
}

/// Converts ML Kit output into [OcrResult], dropping blank lines.
OcrResult mapRecognizedText(
  RecognizedText recognized,
  ui.Size decodedSize,
  Duration duration,
) {
  final lines = <OcrLine>[];
  for (final (blockIndex, block) in recognized.blocks.indexed) {
    for (final (lineIndex, line) in block.lines.indexed) {
      final text = line.text.trim();
      if (text.isEmpty) {
        continue;
      }
      lines.add(
        OcrLine(
          text: text,
          box: line.boundingBox,
          blockIndex: blockIndex,
          lineIndex: lineIndex,
          confidence: line.confidence,
        ),
      );
    }
  }
  return OcrResult(
    imageSize: resolveUprightSize(decodedSize, lines.map((l) => l.box)),
    lines: lines,
    duration: duration,
  );
}

/// ML Kit reports boxes for the upright image, but the decoded header size may
/// be the pre-rotation size when the JPEG carries an EXIF rotation. If the
/// boxes only fit the swapped dimensions, the image was rotated by 90°.
ui.Size resolveUprightSize(ui.Size decoded, Iterable<ui.Rect> boxes) {
  if (boxes.isEmpty) {
    return decoded;
  }
  final maxRight = boxes.map((b) => b.right).reduce(math.max);
  final maxBottom = boxes.map((b) => b.bottom).reduce(math.max);
  bool fits(double width, double height) =>
      maxRight <= width * 1.02 && maxBottom <= height * 1.02;

  if (!fits(decoded.width, decoded.height) &&
      fits(decoded.height, decoded.width)) {
    return ui.Size(decoded.height, decoded.width);
  }
  return decoded;
}

final textRecognitionServiceProvider = Provider<TextRecognitionService>((ref) {
  final service = TextRecognitionService();
  ref.onDispose(service.close);
  return service;
});
