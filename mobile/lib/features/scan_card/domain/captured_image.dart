import 'dart:io';

enum CardImageSource { camera, gallery }

/// A business-card photo held only for the duration of the scan flow.
///
/// The file is a temporary copy written by the picker; [discard] deletes it
/// so no card image outlives the scan.
class CapturedImage {
  const CapturedImage({
    required this.path,
    required this.source,
    required this.isTemporaryCopy,
  });

  final String path;
  final CardImageSource source;

  /// True only when [path] lies inside the app's own temp/cache directory.
  /// Guards against ever deleting a user's original photo.
  final bool isTemporaryCopy;

  Future<void> discard() async {
    if (!isTemporaryCopy) {
      return;
    }
    try {
      await File(path).delete();
    } on FileSystemException {
      // Already gone (OS cache eviction); nothing to clean up.
    }
  }
}
