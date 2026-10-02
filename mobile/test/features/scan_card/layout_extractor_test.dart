import 'dart:ui';

import 'package:business_card_scanner/features/scan_card/domain/layout_extractor.dart';
import 'package:business_card_scanner/features/scan_card/domain/layout_line.dart';
import 'package:business_card_scanner/features/scan_card/domain/ocr_result.dart';
import 'package:flutter_test/flutter_test.dart';

OcrLine line(
  String text,
  double left,
  double top,
  double right,
  double bottom, {
  required int block,
  int index = 0,
}) => OcrLine(
  text: text,
  box: Rect.fromLTRB(left, top, right, bottom),
  blockIndex: block,
  lineIndex: index,
);

/// A centred card with a right-aligned website, in deliberately shuffled
/// ML Kit block order. Text extent: x 0..1000, y 0..1000; median height 40.
List<OcrLine> centredCard({double dx = 0, double dy = 0}) {
  OcrLine at(
    String t,
    double l,
    double tp,
    double r,
    double b,
    int block, [
    int i = 0,
  ]) => line(t, l + dx, tp + dy, r + dx, b + dy, block: block, index: i);
  return [
    at('+90 532 123 45 67', 0, 960, 500, 1000, 0),
    at('Ahmet Yılmaz', 300, 400, 700, 460, 1),
    at('Enterprise Solutions Manager', 250, 470, 750, 510, 1, 1),
    at('ACME TECHNOLOGY', 300, 0, 700, 100, 2),
    at('www.acme.com.tr', 700, 960, 1000, 1000, 3),
    at('ahmet.yilmaz@acme.com.tr', 0, 900, 600, 940, 4),
  ];
}

Map<String, LayoutLine> byText(List<LayoutLine> lines) => {
  for (final l in lines) l.text: l,
};

void main() {
  group('reading order', () {
    test('orders shuffled ML Kit blocks top-to-bottom, rows left-to-right', () {
      final layout = LayoutExtractor.extract(centredCard());

      expect(layout.map((l) => l.text), [
        'ACME TECHNOLOGY',
        'Ahmet Yılmaz',
        'Enterprise Solutions Manager',
        'ahmet.yilmaz@acme.com.tr',
        '+90 532 123 45 67',
        'www.acme.com.tr',
      ]);
      expect(layout.map((l) => l.order), [1, 2, 3, 4, 5, 6]);
    });

    test('keeps ML Kit line order inside a block', () {
      final layout = LayoutExtractor.extract([
        line(
          'Maslak Mah. Büyükdere Cad. No:255',
          0,
          100,
          800,
          140,
          block: 0,
          index: 0,
        ),
        line('34485 Sarıyer / İstanbul', 0, 98, 600, 138, block: 0, index: 1),
      ]);

      expect(layout.first.text, startsWith('Maslak'));
    });

    test('reads an obvious two-column card column by column', () {
      final layout = LayoutExtractor.extract([
        line('+90 212 555 00 00', 600, 0, 1000, 40, block: 0),
        line('Zeynep Kaya', 0, 0, 400, 60, block: 1),
        line('Satış Direktörü', 0, 70, 400, 110, block: 1, index: 1),
        line('zeynep@nova.com.tr', 600, 60, 1000, 100, block: 2),
        line('Satış', 0, 150, 350, 190, block: 3),
        line('www.nova.com.tr', 600, 120, 1000, 160, block: 4),
      ]);

      expect(layout.map((l) => l.text), [
        'Zeynep Kaya',
        'Satış Direktörü',
        'Satış',
        '+90 212 555 00 00',
        'zeynep@nova.com.tr',
        'www.nova.com.tr',
      ]);
      // The start of the right column jumps back up.
      expect(byText(layout)['+90 212 555 00 00']!.gap, LayoutGap.large);
    });

    test('a full-width header stays above the columns', () {
      final layout = LayoutExtractor.extract([
        line('Zeynep Kaya', 0, 200, 400, 260, block: 0),
        line('+90 212 555 00 00', 600, 200, 1000, 240, block: 1),
        line('NOVA ENERJİ YATIRIMLARI A.Ş.', 50, 0, 950, 100, block: 2),
        line('zeynep@nova.com.tr', 600, 260, 1000, 300, block: 3),
      ]);

      expect(layout.map((l) => l.text), [
        'NOVA ENERJİ YATIRIMLARI A.Ş.',
        'Zeynep Kaya',
        '+90 212 555 00 00',
        'zeynep@nova.com.tr',
      ]);
    });

    test('groups stacked vertically are not mistaken for columns', () {
      // Name block top-left, contact block bottom-right: no vertical overlap.
      final layout = LayoutExtractor.extract([
        line('info@acme.com', 600, 800, 1000, 840, block: 0),
        line('Ali Veli', 0, 0, 400, 60, block: 1),
        line('CTO', 0, 70, 200, 110, block: 1, index: 1),
        line('+90 216 000 00 00', 600, 850, 1000, 890, block: 2),
      ]);

      expect(layout.map((l) => l.text), [
        'Ali Veli',
        'CTO',
        'info@acme.com',
        '+90 216 000 00 00',
      ]);
    });
  });

  group('features', () {
    test('region, size and alignment of a centred card', () {
      final layout = byText(LayoutExtractor.extract(centredCard()));

      final company = layout['ACME TECHNOLOGY']!;
      expect(
        (company.region, company.size, company.align),
        (LayoutRegion.top, LayoutSize.large, LayoutAlign.center),
      );
      final name = layout['Ahmet Yılmaz']!;
      expect(
        (name.region, name.size, name.align),
        (LayoutRegion.center, LayoutSize.large, LayoutAlign.center),
      );
      final title = layout['Enterprise Solutions Manager']!;
      expect(
        (title.region, title.size),
        (LayoutRegion.center, LayoutSize.normal),
      );
      final email = layout['ahmet.yilmaz@acme.com.tr']!;
      expect(
        (email.region, email.size, email.align),
        (LayoutRegion.bottom, LayoutSize.normal, LayoutAlign.left),
      );
      expect(layout['www.acme.com.tr']!.align, LayoutAlign.right);
    });

    test('text much smaller than the median is small', () {
      final layout = byText(
        LayoutExtractor.extract([
          line('Ayşe Demir', 0, 0, 500, 40, block: 0),
          line('Proje Yöneticisi', 0, 50, 500, 90, block: 1),
          line('Ltd. Şti.', 0, 100, 200, 125, block: 2),
        ]),
      );

      expect(layout['Ltd. Şti.']!.size, LayoutSize.small);
    });

    test('sameBlock and gap describe grouping', () {
      final layout = byText(LayoutExtractor.extract(centredCard()));

      expect(layout['ACME TECHNOLOGY']!.sameBlock, isFalse);
      expect(layout['ACME TECHNOLOGY']!.gap, LayoutGap.none);
      expect(layout['Ahmet Yılmaz']!.gap, LayoutGap.large);
      expect(layout['Enterprise Solutions Manager']!.sameBlock, isTrue);
      expect(layout['Enterprise Solutions Manager']!.gap, LayoutGap.none);
      expect(layout['+90 532 123 45 67']!.gap, LayoutGap.none);
      // Next block on the same row: not a column jump.
      expect(layout['www.acme.com.tr']!.gap, LayoutGap.none);
      expect(layout['www.acme.com.tr']!.sameBlock, isFalse);
    });

    test('a moderate gap is small', () {
      final layout = LayoutExtractor.extract([
        line('Mehmet Öz', 0, 0, 400, 40, block: 0),
        line('Genel Müdür', 0, 80, 400, 120, block: 1),
      ]);

      expect(layout.last.gap, LayoutGap.small);
    });

    test('results do not depend on where the card sits in the photo', () {
      final framed = LayoutExtractor.extract(centredCard());
      final loose = LayoutExtractor.extract(centredCard(dx: 900, dy: 2400));

      String signature(LayoutLine l) =>
          '${l.order}|${l.region}|${l.size}|${l.align}|${l.sameBlock}|${l.gap}';
      expect(loose.map(signature), framed.map(signature));
    });

    test('a single line is centred, normal and has no gap', () {
      final only = LayoutExtractor.extract([
        line('NOVA', 100, 100, 300, 160, block: 0),
      ]).single;

      expect(
        (only.order, only.region, only.size, only.sameBlock, only.gap),
        (1, LayoutRegion.center, LayoutSize.normal, false, LayoutGap.none),
      );
    });

    test('no lines produce no layout', () {
      expect(LayoutExtractor.extract(const []), isEmpty);
    });
  });

  group('ambiguous one-word company: layout decides', () {
    test('NOVA below the person, at the bottom', () {
      final layout = LayoutExtractor.extract([
        line('Ahmet Yılmaz', 100, 0, 700, 70, block: 0),
        line('General Manager', 100, 80, 600, 120, block: 0, index: 1),
        line('NOVA', 100, 400, 400, 500, block: 1),
      ]);

      final nova = layout.last;
      expect(nova.text, 'NOVA');
      expect(
        (nova.order, nova.region, nova.size),
        (3, LayoutRegion.bottom, LayoutSize.large),
      );
      expect(nova.gap, LayoutGap.large);
    });

    test('NOVA above the person, at the top', () {
      final layout = LayoutExtractor.extract([
        line('Ahmet Yılmaz', 100, 380, 700, 450, block: 1),
        line('General Manager', 100, 460, 600, 500, block: 1, index: 1),
        line('NOVA', 100, 0, 400, 100, block: 0),
      ]);

      final nova = layout.first;
      expect(nova.text, 'NOVA');
      expect(
        (nova.order, nova.region, nova.size),
        (1, LayoutRegion.top, LayoutSize.large),
      );
      expect(layout[1].region, isNot(LayoutRegion.top));
    });
  });

  group('wire contract', () {
    test('exposes exactly the agreed keys and no coordinates', () {
      final json = LayoutExtractor.extract(centredCard()).first.toJson();

      expect(json.keys.toSet(), {
        'text',
        'order',
        'region',
        'size',
        'align',
        'sameBlock',
        'gap',
      });
      // The only number allowed on the wire is the reading order.
      final numericKeys = json.entries
          .where((e) => e.value is num)
          .map((e) => e.key);
      expect(numericKeys, ['order']);
      expect(json['region'], 'top');
      expect(json['size'], 'large');
      expect(json['align'], 'center');
      expect(json['sameBlock'], false);
      expect(json['gap'], 'none');
    });

    test('text is passed through unchanged, including Turkish characters', () {
      final layout = LayoutExtractor.extract([
        line('ÇAĞLAR ŞENGÜL — İĞDE ÖZÜ', 0, 0, 100, 20, block: 0),
      ]);

      expect(layout.single.toJson()['text'], 'ÇAĞLAR ŞENGÜL — İĞDE ÖZÜ');
    });

    test('tag form matches the LLM prompt format', () {
      final first = LayoutExtractor.extract(centredCard()).first;

      expect(first.toString(), '[TOP][LARGE] ACME TECHNOLOGY');
    });
  });
}
