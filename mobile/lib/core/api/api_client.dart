import 'package:dio/dio.dart';

import '../errors/api_failure.dart';

/// Supplies a bearer token; `forceRefresh` bypasses the in-memory cache.
///
/// Must throw [ApiFailure]: code [ApiFailure.sessionExpired] when the user has
/// to sign in again, any other code (e.g. network) for transient problems.
typedef AccessTokenProvider = Future<String> Function({bool forceRefresh});

/// Builds the single Dio instance used for BusinessCard.Api. One instance
/// means one connection pool, so TLS connections are reused across calls.
Dio createApiClient({
  required String baseUrl,
  required AccessTokenProvider accessToken,
  required Future<void> Function() onSessionExpired,
  HttpClientAdapter? httpClientAdapter,
}) {
  final dio = Dio(
    BaseOptions(
      baseUrl: baseUrl,
      connectTimeout: const Duration(seconds: 10),
      sendTimeout: const Duration(seconds: 10),
      // The parse endpoint has its own server-side LLM timeout; this only
      // guards against a hung connection.
      receiveTimeout: const Duration(seconds: 20),
      contentType: Headers.jsonContentType,
      responseType: ResponseType.json,
    ),
  );
  if (httpClientAdapter != null) {
    dio.httpClientAdapter = httpClientAdapter;
  }
  dio.interceptors.add(
    AuthInterceptor(
      dio: dio,
      accessToken: accessToken,
      onSessionExpired: onSessionExpired,
    ),
  );
  return dio;
}

/// Attaches the bearer token and recovers once from a 401 by forcing a token
/// refresh (e.g. token revoked server-side before its expiry). A second 401
/// means the session is gone and the user is signed out.
class AuthInterceptor extends Interceptor {
  AuthInterceptor({
    required this._dio,
    required this._accessToken,
    required this._onSessionExpired,
  });

  static const _retriedKey = 'auth.retried';

  final Dio _dio;
  final AccessTokenProvider _accessToken;
  final Future<void> Function() _onSessionExpired;

  @override
  Future<void> onRequest(
    RequestOptions options,
    RequestInterceptorHandler handler,
  ) async {
    if (options.extra[_retriedKey] == true) {
      // Retry after a forced refresh: keep the token onError just attached.
      handler.next(options);
      return;
    }
    try {
      options.headers['Authorization'] =
          'Bearer ${await _accessToken(forceRefresh: false)}';
      handler.next(options);
    } on ApiFailure catch (failure) {
      handler.reject(await _failed(options, failure), true);
    }
  }

  @override
  Future<void> onError(
    DioException err,
    ErrorInterceptorHandler handler,
  ) async {
    final options = err.requestOptions;
    if (err.response?.statusCode != 401) {
      handler.next(err);
      return;
    }
    if (options.extra[_retriedKey] == true) {
      // Fresh token still rejected: the session is not usable any more.
      await _onSessionExpired();
      handler.next(err);
      return;
    }

    try {
      final token = await _accessToken(forceRefresh: true);
      options
        ..headers['Authorization'] = 'Bearer $token'
        ..extra[_retriedKey] = true;
      handler.resolve(await _dio.fetch<dynamic>(options));
    } on ApiFailure catch (failure) {
      handler.next(await _failed(options, failure));
    } on DioException catch (retryError) {
      handler.next(retryError);
    }
  }

  Future<DioException> _failed(
    RequestOptions options,
    ApiFailure failure,
  ) async {
    if (failure.code == ApiFailure.sessionExpired) {
      await _onSessionExpired();
    }
    return DioException(requestOptions: options, error: failure);
  }
}
