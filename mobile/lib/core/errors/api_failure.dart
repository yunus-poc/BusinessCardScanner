import 'package:dio/dio.dart';

/// A failed API call translated into something the UI can act on.
///
/// [code] mirrors the backend's ProblemDetails `code` (e.g. `GRAPH_RATE_LIMITED`)
/// or a client-side code for failures that never reached the API.
class ApiFailure implements Exception {
  const ApiFailure({
    required this.code,
    required this.message,
    required this.retryable,
    this.statusCode,
    this.traceId,
  });

  factory ApiFailure.fromDioException(DioException exception) {
    if (exception.error case final ApiFailure failure) {
      return failure;
    }

    switch (exception.type) {
      case DioExceptionType.connectionTimeout:
      case DioExceptionType.sendTimeout:
      case DioExceptionType.receiveTimeout:
      case DioExceptionType.transformTimeout:
        return const ApiFailure(
          code: timeout,
          message: 'The server took too long to respond. Please try again.',
          retryable: true,
        );
      case DioExceptionType.connectionError:
        return const ApiFailure(
          code: networkUnavailable,
          message:
              'No connection to the server. Check your network and try again.',
          retryable: true,
        );
      case DioExceptionType.cancel:
        return const ApiFailure(
          code: cancelled,
          message: 'Request cancelled.',
          retryable: false,
        );
      case DioExceptionType.badCertificate:
        return const ApiFailure(
          code: networkUnavailable,
          message: 'Secure connection to the server failed.',
          retryable: false,
        );
      case DioExceptionType.badResponse:
      case DioExceptionType.unknown:
        return _fromResponse(exception.response);
    }
  }

  static const networkUnavailable = 'NETWORK_UNAVAILABLE';
  static const timeout = 'TIMEOUT';
  static const cancelled = 'CANCELLED';
  static const sessionExpired = 'SESSION_EXPIRED';
  static const unknown = 'UNKNOWN';

  final String code;

  /// Safe to show to the user.
  final String message;

  /// True when retrying the same request can succeed (server says so via
  /// ProblemDetails `retryable`, or the failure was network-level).
  final bool retryable;

  final int? statusCode;

  /// Correlates with server logs; show it on error screens for support.
  final String? traceId;

  static ApiFailure _fromResponse(Response<dynamic>? response) {
    final status = response?.statusCode;
    final body = response?.data;
    final problem = body is Map<String, dynamic>
        ? body
        : const <String, dynamic>{};
    final statusRetryable =
        status != null && const {408, 429, 502, 503, 504}.contains(status);

    return ApiFailure(
      code: problem['code'] as String? ?? unknown,
      message:
          problem['detail'] as String? ??
          problem['title'] as String? ??
          'Something went wrong. Please try again.',
      retryable: problem['retryable'] as bool? ?? statusRetryable,
      statusCode: status,
      traceId:
          problem['traceId'] as String? ??
          response?.headers.value('x-trace-id'),
    );
  }

  @override
  String toString() =>
      'ApiFailure($code, status: $statusCode, traceId: $traceId)';
}
