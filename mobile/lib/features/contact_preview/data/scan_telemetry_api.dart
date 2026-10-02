import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../app/api_provider.dart';
import '../domain/parsed_card.dart';

/// Reports on-device timings so the latency target is measured in production.
/// Numbers and a status only; never card content. Best effort: failures are
/// ignored and never affect the user.
class ScanTelemetryApi {
  ScanTelemetryApi(this._dio);

  final Dio _dio;

  Future<void> reportScan({
    required Duration ocr,
    required Duration ocrToPreview,
    required LlmStatus llmStatus,
  }) async {
    try {
      await _dio.post<void>(
        '/api/telemetry/scan',
        data: {
          'ocrMs': ocr.inMilliseconds.clamp(0, 60000),
          'ocrToPreviewMs': ocrToPreview.inMilliseconds.clamp(0, 120000),
          'llmStatus': llmStatus.name,
        },
      );
    } on DioException {
      // Telemetry must never surface as an error.
    }
  }
}

final scanTelemetryApiProvider = Provider<ScanTelemetryApi>(
  (ref) => ScanTelemetryApi(ref.watch(apiClientProvider)),
);
