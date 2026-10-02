import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../data/card_image_picker.dart';
import '../data/text_recognition_service.dart';
import '../domain/captured_image.dart';
import '../domain/layout_extractor.dart';
import '../domain/scan_state.dart';

/// Drives capture → on-device OCR, and owns the card image for its whole
/// (short) life: the file is deleted as soon as OCR succeeds, on retake, on
/// leaving the flow ([reset]) and when the controller is disposed.
class ScanController extends Notifier<ScanState> {
  static const noTextMessage =
      'No text found. Retake the photo with the card filling the frame, '
      'in good light and without glare.';
  static const ocrFailedMessage = 'Could not read the card. Please try again.';

  CardImagePicker get _picker => ref.read(cardImagePickerProvider);
  TextRecognitionService get _ocr => ref.read(textRecognitionServiceProvider);

  /// Tracked separately from [state] so disposal can clean up without
  /// touching state after the notifier is gone.
  CapturedImage? _current;

  @override
  ScanState build() {
    ref.onDispose(() => _current?.discard());
    return const ScanIdle();
  }

  /// Returns true when an image was accepted and recognition has started,
  /// i.e. the caller should show the scan screen.
  Future<bool> capture(CardImageSource source) async {
    if (state is ScanPicking || state is ScanRecognizing) {
      return false;
    }
    // Picking first, so a second tap is ignored while the old file is deleted.
    state = ScanPicking(source);
    await _discardCurrent();
    try {
      return _start(await _picker.pick(source));
    } on ImageAccessException catch (e) {
      _set(ScanFailed(e.message));
      return false;
    }
  }

  /// Picks up a photo taken just before Android killed the app.
  Future<bool> recoverLostImage() async {
    if (state is! ScanIdle) {
      return false;
    }
    return _start(await _picker.recoverLostImage());
  }

  /// Runs OCR again on an image kept after a recognizer error.
  void retryRecognition() {
    if (state case ScanOcrFailed(:final retainedImage?)) {
      _start(retainedImage);
    }
  }

  /// Leaves the scan flow and deletes any image. A pending camera/photo access
  /// error is kept so the home screen can still show it.
  Future<void> reset() async {
    await _discardCurrent();
    if (state is! ScanFailed) {
      _set(const ScanIdle());
    }
  }

  /// Called once the UI has shown a [ScanFailed] message.
  void acknowledgeError() {
    if (state is ScanFailed) {
      _set(const ScanIdle());
    }
  }

  bool _start(CapturedImage? image) {
    if (!ref.mounted) {
      image?.discard();
      return false;
    }
    _current = image;
    if (image == null) {
      state = const ScanIdle();
      return false;
    }
    state = ScanRecognizing(image);
    unawaited(_recognize(image));
    return true;
  }

  Future<void> _recognize(CapturedImage image) async {
    try {
      final result = await _ocr.recognize(image);
      if (!_isCurrent(image)) {
        return;
      }
      _set(
        result.isEmpty
            ? ScanOcrFailed(message: noTextMessage, source: image.source)
            : ScanRecognized(
                result,
                LayoutExtractor.extract(result.lines),
                image.source,
              ),
      );
      // Text and geometry are all we need from here on. Deleting does not
      // block showing the result.
      unawaited(_discardCurrent());
    } on OcrFailedException {
      if (_isCurrent(image)) {
        _set(
          ScanOcrFailed(
            message: ocrFailedMessage,
            source: image.source,
            retainedImage: image,
          ),
        );
      }
    }
  }

  /// False when the user retook or left while OCR was running.
  bool _isCurrent(CapturedImage image) =>
      ref.mounted && identical(_current, image);

  Future<void> _discardCurrent() async {
    final image = _current;
    _current = null;
    await image?.discard();
  }

  void _set(ScanState next) {
    if (ref.mounted) {
      state = next;
    }
  }
}

final scanControllerProvider = NotifierProvider<ScanController, ScanState>(
  ScanController.new,
);
