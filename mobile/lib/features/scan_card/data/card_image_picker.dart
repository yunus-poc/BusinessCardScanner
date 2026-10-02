import 'dart:io';

import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:image_picker/image_picker.dart';
import 'package:path/path.dart' as p;
import 'package:path_provider/path_provider.dart';

import '../domain/captured_image.dart';

/// The user denied camera or photo access, or the device has no camera.
class ImageAccessException implements Exception {
  const ImageAccessException(this.message);

  /// Safe to display.
  final String message;
}

/// Obtains a card photo from the system camera or photo picker.
///
/// Uses the OS camera rather than an in-app preview: focus, flash and
/// orientation are handled by the platform, and Android needs no CAMERA
/// permission for the capture intent.
class CardImagePicker {
  CardImagePicker({
    ImagePicker? picker,
    Future<Directory> Function()? temporaryDirectory,
    bool? isAndroid,
  }) : _picker = picker ?? ImagePicker(),
       _temporaryDirectory = temporaryDirectory ?? getTemporaryDirectory,
       _isAndroid = isAndroid ?? Platform.isAndroid;

  /// Long edge in pixels. Plenty for ML Kit on a business card, and much
  /// faster to recognize than a 12-MP original.
  static const maxDimension = 2048.0;
  static const jpegQuality = 90;

  final ImagePicker _picker;
  final Future<Directory> Function() _temporaryDirectory;
  final bool _isAndroid;

  /// Returns null when the user backs out of the camera or picker.
  Future<CapturedImage?> pick(CardImageSource source) async {
    final XFile? file;
    try {
      file = await _picker.pickImage(
        source: source == CardImageSource.camera
            ? ImageSource.camera
            : ImageSource.gallery,
        maxWidth: maxDimension,
        maxHeight: maxDimension,
        imageQuality: jpegQuality,
        // No EXIF/GPS metadata and no photo-library permission on iOS.
        requestFullMetadata: false,
      );
    } on PlatformException catch (e) {
      throw ImageAccessException(_messageFor(e.code, source));
    }
    return file == null ? null : _toCaptured(file, source);
  }

  /// Android may kill the app while the camera is open; the photo is then
  /// delivered on the next launch. Always null on iOS.
  Future<CapturedImage?> recoverLostImage() async {
    if (!_isAndroid) {
      return null;
    }
    final response = await _picker.retrieveLostData();
    final file = response.file;
    return file == null ? null : _toCaptured(file, CardImageSource.camera);
  }

  Future<CapturedImage> _toCaptured(XFile file, CardImageSource source) async {
    final tempDir = p.canonicalize((await _temporaryDirectory()).path);
    return CapturedImage(
      path: file.path,
      source: source,
      isTemporaryCopy: p.isWithin(tempDir, p.canonicalize(file.path)),
    );
  }

  static String _messageFor(String code, CardImageSource source) {
    return switch (code) {
      'camera_access_denied' =>
        'Camera access is off. Allow it in Settings → Business Cards → Camera.',
      'photo_access_denied' =>
        'Photo access is off. Allow it in Settings → Business Cards → Photos.',
      'no_available_camera' => 'No camera is available on this device.',
      _ =>
        source == CardImageSource.camera
            ? 'Could not open the camera. Please try again.'
            : 'Could not open your photos. Please try again.',
    };
  }
}

final cardImagePickerProvider = Provider<CardImagePicker>(
  (ref) => CardImagePicker(),
);
