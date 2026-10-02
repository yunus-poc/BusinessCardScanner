import 'package:business_card_scanner/core/errors/api_failure.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  final request = RequestOptions(path: '/api/contacts');

  DioException badResponse(
    int status,
    Object? data, {
    Map<String, List<String>>? headers,
  }) => DioException(
    requestOptions: request,
    type: DioExceptionType.badResponse,
    response: Response<dynamic>(
      requestOptions: request,
      statusCode: status,
      data: data,
      headers: Headers.fromMap(headers ?? {}),
    ),
  );

  test('reads code, detail, retryable and traceId from ProblemDetails', () {
    final failure = ApiFailure.fromDioException(
      badResponse(429, {
        'title': 'Too Many Requests',
        'detail': 'Outlook is busy.',
        'code': 'GRAPH_RATE_LIMITED',
        'retryable': true,
        'traceId': 'abc123',
      }),
    );

    expect(failure.code, 'GRAPH_RATE_LIMITED');
    expect(failure.message, 'Outlook is busy.');
    expect(failure.retryable, isTrue);
    expect(failure.statusCode, 429);
    expect(failure.traceId, 'abc123');
  });

  test('falls back to title, status-based retryability and trace header', () {
    final failure = ApiFailure.fromDioException(
      badResponse(
        503,
        {'title': 'Service Unavailable'},
        headers: {
          'x-trace-id': ['from-header'],
        },
      ),
    );

    expect(failure.message, 'Service Unavailable');
    expect(failure.code, ApiFailure.unknown);
    expect(failure.retryable, isTrue);
    expect(failure.traceId, 'from-header');
  });

  test('non-JSON error bodies still produce a safe message', () {
    final failure = ApiFailure.fromDioException(
      badResponse(500, '<html>proxy error</html>'),
    );

    expect(failure.message, isNot(contains('html')));
    expect(failure.retryable, isFalse);
  });

  test('network-level failures are retryable', () {
    for (final type in [
      DioExceptionType.connectionError,
      DioExceptionType.connectionTimeout,
      DioExceptionType.receiveTimeout,
    ]) {
      final failure = ApiFailure.fromDioException(
        DioException(requestOptions: request, type: type),
      );
      expect(failure.retryable, isTrue, reason: '$type');
    }
  });

  test('an ApiFailure carried by the DioException is returned as-is', () {
    const original = ApiFailure(
      code: ApiFailure.sessionExpired,
      message: 'expired',
      retryable: false,
    );

    expect(
      ApiFailure.fromDioException(
        DioException(requestOptions: request, error: original),
      ),
      same(original),
    );
  });
}
