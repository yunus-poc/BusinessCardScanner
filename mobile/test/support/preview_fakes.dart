import 'dart:async';

import 'package:business_card_scanner/core/errors/api_failure.dart';
import 'package:business_card_scanner/features/contact_preview/data/business_card_api.dart';
import 'package:business_card_scanner/features/contact_preview/data/scan_telemetry_api.dart';
import 'package:business_card_scanner/features/contact_preview/domain/parsed_card.dart';
import 'package:business_card_scanner/features/scan_card/domain/layout_line.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

/// API response as the backend would send it.
Map<String, dynamic> parseResponse({
  Map<String, (String, double, bool)> fields = const {},
  List<String> additionalEmails = const [],
  List<String> additionalPhones = const [],
  bool fallbackUsed = false,
  String llmStatus = 'ok',
}) => {
  'contact': {
    for (final MapEntry(:key, value: (value, confidence, review))
        in fields.entries)
      key: {'value': value, 'confidence': confidence, 'needsReview': review},
  },
  'additionalEmails': additionalEmails,
  'additionalPhones': additionalPhones,
  'meta': {
    'llmUsed': !fallbackUsed,
    'fallbackUsed': fallbackUsed,
    'llmStatus': llmStatus,
  },
};

final sampleCard = ParsedCard.fromJson(
  parseResponse(
    fields: {
      'firstName': ('Ahmet', 0.97, false),
      'lastName': ('Yılmaz', 0.97, false),
      'companyName': ('ACME Technology', 0.92, false),
      'jobTitle': ('Enterprise Solutions Manager', 0.72, true),
      'email': ('ahmet.yilmaz@acme.com.tr', 0.97, false),
      'mobilePhone': ('+90 532 123 45 67', 0.95, false),
      'city': ('İstanbul', 0.6, true),
    },
    additionalEmails: ['satis@acme.com.tr'],
  ),
);

class FakeBusinessCardApi extends Fake implements BusinessCardApi {
  /// Each call takes the next entry: a [ParsedCard], an [ApiFailure] to throw,
  /// or a [Completer] to control timing. Falls back to [defaultCard].
  final List<Object> results = [];
  ParsedCard defaultCard = sampleCard;
  final List<List<LayoutLine>> requests = [];

  @override
  Future<ParsedCard> parse(
    List<LayoutLine> lines, {
    CancelToken? cancelToken,
  }) async {
    requests.add(lines);
    final next = results.isEmpty ? defaultCard : results.removeAt(0);
    return switch (next) {
      final ParsedCard card => card,
      final Completer<ParsedCard> completer => completer.future,
      final ApiFailure failure => throw failure,
      _ => throw StateError('Unsupported fake result $next'),
    };
  }
}

class FakeScanTelemetryApi extends Fake implements ScanTelemetryApi {
  final List<(Duration, Duration, LlmStatus)> reports = [];

  @override
  Future<void> reportScan({
    required Duration ocr,
    required Duration ocrToPreview,
    required LlmStatus llmStatus,
  }) async => reports.add((ocr, ocrToPreview, llmStatus));
}

const networkFailure = ApiFailure(
  code: ApiFailure.networkUnavailable,
  message: 'No connection to the server.',
  retryable: true,
  traceId: 'trace-123',
);

const validationFailure = ApiFailure(
  code: 'VALIDATION_FAILED',
  message: 'The request was invalid.',
  retryable: false,
);
