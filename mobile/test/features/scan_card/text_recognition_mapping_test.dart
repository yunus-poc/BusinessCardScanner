import 'dart:ui';

import 'package:business_card_scanner/features/scan_card/data/text_recognition_service.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:google_mlkit_text_recognition/google_mlkit_text_recognition.dart';

TextLine _line(String text, Rect box, {double? confidence}) => TextLine(
  text: text,
  elements: const [],
  boundingBox: box,
  recognizedLanguages: const [],
  cornerPoints: const [],
  confidence: confidence,
  angle: null,
);

TextBlock _block(List<TextLine> lines) => TextBlock(
  text: lines.map((l) => l.text).join('\n'),
  lines: lines,
  boundingBox: lines
      .map((l) => l.boundingBox)
      .reduce((a, b) => a.expandToInclude(b)),
  recognizedLanguages: const [],
  cornerPoints: const [],
);

void main() {
  group('mapRecognizedText', () {
    test('flattens blocks in ML Kit order and keeps block/line indexes', () {
      final recognized = RecognizedText(
        text: '',
        blocks: [
          _block([
            _line('ACME TECHNOLOGY', const Rect.fromLTRB(100, 80, 900, 160)),
          ]),
          _block([
            _line(
              'Ahmet Yılmaz',
              const Rect.fromLTRB(100, 400, 700, 470),
              confidence: 0.97,
            ),
            _line(
              'Enterprise Solutions Manager',
              const Rect.fromLTRB(100, 480, 900, 520),
            ),
          ]),
        ],
      );

      final result = mapRecognizedText(
        recognized,
        const Size(2048, 1152),
        const Duration(milliseconds: 210),
      );

      expect(result.lines.map((l) => l.text), [
        'ACME TECHNOLOGY',
        'Ahmet Yılmaz',
        'Enterprise Solutions Manager',
      ]);
      expect(result.lines.map((l) => l.blockIndex), [0, 1, 1]);
      expect(result.lines.map((l) => l.lineIndex), [0, 0, 1]);
      expect(result.lines[1].confidence, 0.97);
      expect(result.lines[1].box, const Rect.fromLTRB(100, 400, 700, 470));
      expect(result.duration.inMilliseconds, 210);
    });

    test('preserves Turkish characters exactly', () {
      final recognized = RecognizedText(
        text: '',
        blocks: [
          _block([
            _line(
              'ÇAĞRI ŞENÖZ — İstanbul Üsküdar',
              const Rect.fromLTRB(0, 0, 100, 20),
            ),
          ]),
        ],
      );

      final result = mapRecognizedText(
        recognized,
        const Size(200, 100),
        Duration.zero,
      );

      expect(result.lines.single.text, 'ÇAĞRI ŞENÖZ — İstanbul Üsküdar');
    });

    test('drops blank lines and trims whitespace', () {
      final recognized = RecognizedText(
        text: '',
        blocks: [
          _block([
            _line('   ', const Rect.fromLTRB(0, 0, 10, 10)),
            _line('  info@acme.com.tr ', const Rect.fromLTRB(0, 20, 100, 30)),
          ]),
        ],
      );

      final result = mapRecognizedText(
        recognized,
        const Size(200, 100),
        Duration.zero,
      );

      expect(result.lines.single.text, 'info@acme.com.tr');
      expect(result.lines.single.lineIndex, 1);
    });

    test('no blocks means an empty result', () {
      final result = mapRecognizedText(
        RecognizedText(text: '', blocks: const []),
        const Size(200, 100),
        Duration.zero,
      );

      expect(result.isEmpty, isTrue);
    });
  });

  group('resolveUprightSize', () {
    test('keeps the decoded size when boxes fit', () {
      final size = resolveUprightSize(const Size(2048, 1152), [
        const Rect.fromLTRB(100, 100, 2000, 1100),
      ]);

      expect(size, const Size(2048, 1152));
    });

    test('swaps width and height when boxes only fit the rotated image', () {
      // Portrait photo stored landscape with an EXIF rotation tag.
      final size = resolveUprightSize(const Size(2048, 1536), [
        const Rect.fromLTRB(100, 1500, 1400, 1900),
      ]);

      expect(size, const Size(1536, 2048));
    });

    test('tolerates boxes that overshoot the edge slightly', () {
      final size = resolveUprightSize(const Size(1000, 500), [
        const Rect.fromLTRB(0, 0, 1010, 505),
      ]);

      expect(size, const Size(1000, 500));
    });

    test('keeps the decoded size when neither orientation fits', () {
      final size = resolveUprightSize(const Size(1000, 500), [
        const Rect.fromLTRB(0, 0, 2000, 2000),
      ]);

      expect(size, const Size(1000, 500));
    });
  });
}
