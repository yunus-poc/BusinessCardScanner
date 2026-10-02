import 'captured_image.dart';
import 'layout_line.dart';
import 'ocr_result.dart';

sealed class ScanState {
  const ScanState();
}

final class ScanIdle extends ScanState {
  const ScanIdle();
}

/// The system camera or photo picker is open.
final class ScanPicking extends ScanState {
  const ScanPicking(this.source);

  final CardImageSource source;
}

/// On-device OCR is running on [image].
final class ScanRecognizing extends ScanState {
  const ScanRecognizing(this.image);

  final CapturedImage image;
}

/// OCR finished. The image has already been deleted; only text, geometry and
/// the derived layout lines remain, in memory.
final class ScanRecognized extends ScanState {
  const ScanRecognized(this.result, this.layout, this.source);

  final OcrResult result;

  /// Reading-ordered lines with compact layout hints: the backend payload.
  final List<LayoutLine> layout;
  final CardImageSource source;
}

/// OCR failed or found no text.
final class ScanOcrFailed extends ScanState {
  const ScanOcrFailed({
    required this.message,
    required this.source,
    this.retainedImage,
  });

  final String message;
  final CardImageSource source;

  /// Kept only when recognizing the same image again can succeed
  /// (a recognizer error); null when the user should take a new photo.
  final CapturedImage? retainedImage;

  bool get canRetry => retainedImage != null;
}

/// Camera/photo access problem, shown on the home screen.
final class ScanFailed extends ScanState {
  const ScanFailed(this.message);

  /// Safe to display.
  final String message;
}
