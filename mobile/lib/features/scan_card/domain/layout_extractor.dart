import 'dart:math' as math;
import 'dart:ui';

import 'layout_line.dart';
import 'ocr_result.dart';

/// Deterministic conversion of OCR geometry into [LayoutLine]s.
///
/// Deliberately small: a handful of thresholds, no document reconstruction.
/// Positions are normalized to the *text extent* (union of all line boxes),
/// not the photo, because a card rarely fills the whole frame; normalizing to
/// the photo would label most lines "center" on a loosely framed shot.
abstract final class LayoutExtractor {
  static const topLimit = 0.33;
  static const centerLimit = 0.66;
  static const smallRatio = 0.8;
  static const largeRatio = 1.25;
  static const noGapRatio = 0.6;
  static const smallGapRatio = 1.5;

  /// Blocks wider than this share of the frame are full-width rows (headers,
  /// addresses), never part of a column.
  static const maxColumnBlockWidth = 0.6;

  /// Minimum empty horizontal band between two columns.
  static const minGutter = 0.04;

  /// Columns must overlap vertically by this share of the shorter one;
  /// otherwise they are stacked groups, not side-by-side columns.
  static const minColumnOverlap = 0.25;

  static List<LayoutLine> extract(List<OcrLine> lines) {
    if (lines.isEmpty) {
      return const [];
    }
    final ordered = readingOrder(lines);
    final frame = _union(ordered.map((l) => l.box));
    final medianHeight = _median(ordered.map((l) => l.box.height));

    return [
      for (final (i, line) in ordered.indexed)
        LayoutLine(
          text: line.text,
          order: i + 1,
          region: _region(line.box, frame),
          size: _size(line.box.height, medianHeight),
          align: _align(line.box, frame),
          sameBlock: i > 0 && ordered[i - 1].blockIndex == line.blockIndex,
          gap: i == 0
              ? LayoutGap.none
              : _gap(ordered[i - 1].box, line.box, medianHeight),
        ),
    ];
  }

  /// ML Kit's line order is kept inside each block (reliable); blocks are
  /// ordered geometrically (ML Kit's block order is not guaranteed):
  /// top-to-bottom, rows left-to-right, and side-by-side columns left first.
  static List<OcrLine> readingOrder(List<OcrLine> lines) {
    final blocks = _blocks(lines);
    final frame = _union(blocks.map((b) => b.box));
    final medianHeight = _median(lines.map((l) => l.box.height));

    final columns = _detectColumns(blocks, frame);
    final List<_Block> ordered;
    if (columns == null) {
      ordered = _rowOrder(blocks, medianHeight);
    } else {
      final inColumns = columns.expand((c) => c).toSet();
      final columnTop = inColumns.map((b) => b.box.top).reduce(math.min);
      // Full-width blocks keep their vertical position around the column set.
      final units = <({double top, List<_Block> blocks})>[
        for (final block in blocks.where((b) => !inColumns.contains(b)))
          (top: block.box.top, blocks: [block]),
        (
          top: columnTop,
          blocks: [
            for (final column in columns)
              ...column..sort((a, b) => a.box.top.compareTo(b.box.top)),
          ],
        ),
      ]..sort((a, b) => a.top.compareTo(b.top));
      ordered = [for (final unit in units) ...unit.blocks];
    }
    return [for (final block in ordered) ...block.lines];
  }

  static List<_Block> _blocks(List<OcrLine> lines) {
    final byIndex = <int, List<OcrLine>>{};
    for (final line in lines) {
      byIndex.putIfAbsent(line.blockIndex, () => []).add(line);
    }
    return [
      for (final blockLines in byIndex.values)
        _Block(
          blockLines..sort((a, b) => a.lineIndex.compareTo(b.lineIndex)),
          _union(blockLines.map((l) => l.box)),
        ),
    ];
  }

  /// Top-to-bottom; blocks whose tops are within half a line height form a
  /// row and are read left-to-right.
  static List<_Block> _rowOrder(List<_Block> blocks, double medianHeight) {
    final byTop = [...blocks]..sort((a, b) => a.box.top.compareTo(b.box.top));
    final result = <_Block>[];
    var row = <_Block>[];
    for (final block in byTop) {
      if (row.isNotEmpty &&
          block.box.top - row.first.box.top > medianHeight * 0.5) {
        result.addAll(row..sort((a, b) => a.box.left.compareTo(b.box.left)));
        row = [];
      }
      row.add(block);
    }
    return result
      ..addAll(row..sort((a, b) => a.box.left.compareTo(b.box.left)));
  }

  /// Returns column groups (left to right) for an obvious multi-column card,
  /// or null. Only narrow blocks can form columns.
  static List<List<_Block>>? _detectColumns(List<_Block> blocks, Rect frame) {
    final narrow =
        blocks
            .where((b) => b.box.width <= frame.width * maxColumnBlockWidth)
            .toList()
          ..sort((a, b) => a.box.left.compareTo(b.box.left));
    if (narrow.length < 2) {
      return null;
    }

    final groups = <List<_Block>>[];
    var groupRight = double.negativeInfinity;
    for (final block in narrow) {
      if (groups.isNotEmpty &&
          block.box.left - groupRight < frame.width * minGutter) {
        groups.last.add(block);
        groupRight = math.max(groupRight, block.box.right);
      } else {
        groups.add([block]);
        groupRight = block.box.right;
      }
    }
    if (groups.length < 2) {
      return null;
    }

    final spans = groups.map((g) => _union(g.map((b) => b.box))).toList();
    for (var i = 1; i < spans.length; i++) {
      if (_verticalOverlap(spans[i - 1], spans[i]) < minColumnOverlap) {
        return null;
      }
    }
    return groups;
  }

  static double _verticalOverlap(Rect a, Rect b) {
    final overlap = math.min(a.bottom, b.bottom) - math.max(a.top, b.top);
    final shorter = math.min(a.height, b.height);
    return shorter <= 0 ? 0 : overlap / shorter;
  }

  static LayoutRegion _region(Rect box, Rect frame) {
    final y = _normalized(box.center.dy, frame.top, frame.height);
    if (y < topLimit) {
      return LayoutRegion.top;
    }
    return y < centerLimit ? LayoutRegion.center : LayoutRegion.bottom;
  }

  static LayoutSize _size(double height, double medianHeight) {
    final ratio = medianHeight <= 0 ? 1.0 : height / medianHeight;
    if (ratio < smallRatio) {
      return LayoutSize.small;
    }
    return ratio > largeRatio ? LayoutSize.large : LayoutSize.normal;
  }

  static LayoutAlign _align(Rect box, Rect frame) {
    final left = _normalized(box.left, frame.left, frame.width);
    final right = _normalized(box.right, frame.left, frame.width);
    final center = (left + right) / 2;
    // A full-width line is centred too, but starting at the edge reads as left.
    if ((center - 0.5).abs() <= 0.1 && left > 0.08) {
      return LayoutAlign.center;
    }
    if (right >= 0.92 && left > 0.25) {
      return LayoutAlign.right;
    }
    return LayoutAlign.left;
  }

  static LayoutGap _gap(Rect previous, Rect current, double medianHeight) {
    if (current.bottom <= previous.top) {
      return LayoutGap.large; // entirely above the previous line: new column
    }
    final distance =
        (current.top - previous.bottom) /
        (medianHeight <= 0 ? 1 : medianHeight);
    // Negative distance = same row (side by side) or overlapping: no gap.
    if (distance < noGapRatio) {
      return LayoutGap.none;
    }
    return distance < smallGapRatio ? LayoutGap.small : LayoutGap.large;
  }

  /// 0.5 for a degenerate extent (e.g. a single line), so it lands in the middle.
  static double _normalized(double value, double origin, double extent) =>
      extent <= 0 ? 0.5 : ((value - origin) / extent).clamp(0.0, 1.0);

  static Rect _union(Iterable<Rect> boxes) =>
      boxes.reduce((a, b) => a.expandToInclude(b));

  static double _median(Iterable<double> values) {
    final sorted = values.toList()..sort();
    final mid = sorted.length ~/ 2;
    return sorted.length.isOdd
        ? sorted[mid]
        : (sorted[mid - 1] + sorted[mid]) / 2;
  }
}

class _Block {
  _Block(this.lines, this.box);

  final List<OcrLine> lines;
  final Rect box;
}
