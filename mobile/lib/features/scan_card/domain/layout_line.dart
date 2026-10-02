/// Compact, semantic layout hints for one OCR line. This is exactly what is
/// sent to the backend: text plus a few categorical values, never pixel
/// coordinates. Enum names are the wire values.
enum LayoutRegion { top, center, bottom }

enum LayoutSize { small, normal, large }

enum LayoutAlign { left, center, right }

/// Vertical space before this line, relative to typical line height.
/// `large` also marks a jump back up (e.g. the start of a second column).
enum LayoutGap { none, small, large }

class LayoutLine {
  const LayoutLine({
    required this.text,
    required this.order,
    required this.region,
    required this.size,
    required this.align,
    required this.sameBlock,
    required this.gap,
  });

  final String text;

  /// 1-based reading order.
  final int order;
  final LayoutRegion region;
  final LayoutSize size;
  final LayoutAlign align;

  /// Same ML Kit block (visual group) as the previous line.
  final bool sameBlock;
  final LayoutGap gap;

  Map<String, Object> toJson() => {
    'text': text,
    'order': order,
    'region': region.name,
    'size': size.name,
    'align': align.name,
    'sameBlock': sameBlock,
    'gap': gap.name,
  };

  /// Compact tag form, as the LLM will see it: `[TOP][LARGE] ACME`.
  @override
  String toString() =>
      '[${region.name.toUpperCase()}][${size.name.toUpperCase()}] $text';
}
