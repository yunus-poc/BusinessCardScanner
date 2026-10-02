import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../app/api_provider.dart';
import '../../../core/errors/api_failure.dart';
import '../../scan_card/domain/layout_line.dart';
import '../domain/parsed_card.dart';

/// BusinessCard.Api parse endpoint. Sends OCR text with compact layout hints
/// only: no image, no coordinates.
class BusinessCardApi {
  BusinessCardApi(this._dio);

  final Dio _dio;

  /// Throws [ApiFailure].
  Future<ParsedCard> parse(
    List<LayoutLine> lines, {
    CancelToken? cancelToken,
  }) async {
    try {
      final response = await _dio.post<Map<String, dynamic>>(
        '/api/business-cards/parse',
        data: {
          'lines': [for (final line in lines) line.toJson()],
        },
        cancelToken: cancelToken,
      );
      return ParsedCard.fromJson(response.data ?? const {});
    } on DioException catch (e) {
      throw ApiFailure.fromDioException(e);
    }
  }
}

final businessCardApiProvider = Provider<BusinessCardApi>(
  (ref) => BusinessCardApi(ref.watch(apiClientProvider)),
);
