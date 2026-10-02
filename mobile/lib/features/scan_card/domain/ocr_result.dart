import 'dart:ui';

/// One recognized text line with its geometry in image pixels.
///
/// Plain app type rather than ML Kit's, so layout extraction and tests never
/// depend on the plugin. Pixel geometry stays on the device; only derived
/// layout features (LayoutExtractor) are sent to the backend.
class OcrLine {
  const OcrLine({
    required this.text,
    required this.box,
    required this.blockIndex,
    required this.lineIndex,
    this.confidence,
  });

  final String text;

  /// Axis-aligned bounding box in upright-image pixel coordinates.
  final Rect box;

  /// ML Kit block (paragraph-like group) this line belongs to, in ML Kit order.
  final int blockIndex;

  /// Position of the line inside its block.
  final int lineIndex;

  /// 0..1 on Android; null on iOS, where ML Kit does not report it.
  final double? confidence;
}

class OcrResult {
  const OcrResult({
    required this.imageSize,
    required this.lines,
    required this.duration,
  });

  /// Size of the upright image the boxes refer to.
  final Size imageSize;

  /// Non-empty lines in ML Kit order (block by block, top line first).
  final List<OcrLine> lines;

  /// Time spent in on-device recognition.
  final Duration duration;

  bool get isEmpty => lines.isEmpty;
}
