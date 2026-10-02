import 'dart:io';

import 'package:business_card_scanner/features/scan_card/data/card_image_picker.dart';
import 'package:business_card_scanner/features/scan_card/domain/captured_image.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:image_picker/image_picker.dart';

class FakeImagePicker extends Fake implements ImagePicker {
  XFile? result;
  PlatformException? error;
  LostDataResponse lostData = LostDataResponse.empty();

  ImageSource? source;
  double? maxWidth;
  double? maxHeight;
  int? imageQuality;
  bool? requestFullMetadata;

  @override
  Future<XFile?> pickImage({
    required ImageSource source,
    double? maxWidth,
    double? maxHeight,
    int? imageQuality,
    CameraDevice preferredCameraDevice = CameraDevice.rear,
    bool requestFullMetadata = true,
  }) async {
    this.source = source;
    this.maxWidth = maxWidth;
    this.maxHeight = maxHeight;
    this.imageQuality = imageQuality;
    this.requestFullMetadata = requestFullMetadata;
    if (error case final e?) {
      throw e;
    }
    return result;
  }

  @override
  Future<LostDataResponse> retrieveLostData() async => lostData;
}

void main() {
  late FakeImagePicker imagePicker;
  late Directory tempDir;

  setUp(() {
    imagePicker = FakeImagePicker();
    tempDir = Directory.systemTemp.createTempSync('bc_cache_');
    addTearDown(() => tempDir.deleteSync(recursive: true));
  });

  CardImagePicker picker({bool isAndroid = true}) => CardImagePicker(
    picker: imagePicker,
    temporaryDirectory: () async => tempDir,
    isAndroid: isAndroid,
  );

  test('downscales, strips metadata and maps the source', () async {
    imagePicker.result = XFile('${tempDir.path}/image_picker_1.jpg');

    final image = await picker().pick(CardImageSource.gallery);

    expect(imagePicker.source, ImageSource.gallery);
    expect(imagePicker.maxWidth, CardImagePicker.maxDimension);
    expect(imagePicker.maxHeight, CardImagePicker.maxDimension);
    expect(imagePicker.imageQuality, CardImagePicker.jpegQuality);
    expect(imagePicker.requestFullMetadata, isFalse);
    expect(image!.source, CardImageSource.gallery);
  });

  test('a file inside the temp directory is a deletable copy', () async {
    imagePicker.result = XFile('${tempDir.path}/sub/image_picker_1.jpg');

    final image = await picker().pick(CardImageSource.camera);

    expect(image!.isTemporaryCopy, isTrue);
  });

  test('a file outside the temp directory is never marked deletable', () async {
    imagePicker.result = XFile('/storage/emulated/0/DCIM/Camera/original.jpg');

    final image = await picker().pick(CardImageSource.gallery);

    expect(image!.isTemporaryCopy, isFalse);
  });

  test('a path that only looks like the temp dir is not deletable', () async {
    imagePicker.result = XFile('${tempDir.path}_evil/../../photo.jpg');

    final image = await picker().pick(CardImageSource.gallery);

    expect(image!.isTemporaryCopy, isFalse);
  });

  test('cancel returns null', () async {
    expect(await picker().pick(CardImageSource.camera), isNull);
  });

  test('permission denial becomes a message pointing to Settings', () async {
    imagePicker.error = PlatformException(code: 'camera_access_denied');

    await expectLater(
      picker().pick(CardImageSource.camera),
      throwsA(
        isA<ImageAccessException>().having(
          (e) => e.message,
          'message',
          contains('Settings'),
        ),
      ),
    );
  });

  test(
    'unknown platform errors get a generic, source-specific message',
    () async {
      imagePicker.error = PlatformException(code: 'weird');

      await expectLater(
        picker().pick(CardImageSource.gallery),
        throwsA(
          isA<ImageAccessException>().having(
            (e) => e.message,
            'message',
            contains('photos'),
          ),
        ),
      );
    },
  );

  test('recovers a lost camera image on Android only', () async {
    imagePicker.lostData = LostDataResponse(
      file: XFile('${tempDir.path}/lost.jpg'),
      type: RetrieveType.image,
    );

    expect(await picker(isAndroid: false).recoverLostImage(), isNull);
    final recovered = await picker().recoverLostImage();
    expect(recovered!.isTemporaryCopy, isTrue);
  });
}
