import 'dart:convert';
import 'dart:typed_data';

import 'package:business_card_scanner/core/api/api_client.dart';
import 'package:business_card_scanner/core/errors/api_failure.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

/// Replays scripted responses and records the Authorization header of each call.
class ScriptedAdapter implements HttpClientAdapter {
  ScriptedAdapter(this._statuses);

  final List<int> _statuses;
  final List<String?> authorizationHeaders = [];

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    authorizationHeaders.add(options.headers['Authorization'] as String?);
    final status = _statuses.removeAt(0);
    return ResponseBody.fromString(
      jsonEncode(
        status == 200
            ? {'ok': true}
            : {'code': 'UNAUTHENTICATED', 'retryable': false},
      ),
      status,
      headers: {
        Headers.contentTypeHeader: [Headers.jsonContentType],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}

void main() {
  late List<bool> tokenRequests;
  late int sessionExpiredCalls;
  late Object? tokenError;

  setUp(() {
    tokenRequests = [];
    sessionExpiredCalls = 0;
    tokenError = null;
  });

  Dio client(ScriptedAdapter adapter) => createApiClient(
    baseUrl: 'https://api.test',
    httpClientAdapter: adapter,
    accessToken: ({forceRefresh = false}) async {
      tokenRequests.add(forceRefresh);
      if (tokenError case final error?) {
        throw error;
      }
      return forceRefresh ? 'fresh-token' : 'cached-token';
    },
    onSessionExpired: () async => sessionExpiredCalls++,
  );

  test('attaches the bearer token', () async {
    final adapter = ScriptedAdapter([200]);

    await client(adapter).get<dynamic>('/api/thing');

    expect(adapter.authorizationHeaders, ['Bearer cached-token']);
    expect(sessionExpiredCalls, 0);
  });

  test('on 401 forces one token refresh and retries the request', () async {
    final adapter = ScriptedAdapter([401, 200]);

    final response = await client(adapter).get<dynamic>('/api/thing');

    expect(response.statusCode, 200);
    expect(adapter.authorizationHeaders, [
      'Bearer cached-token',
      'Bearer fresh-token',
    ]);
    expect(tokenRequests, [false, true]);
    expect(sessionExpiredCalls, 0);
  });

  test('a second 401 signs the user out and surfaces the error', () async {
    final adapter = ScriptedAdapter([401, 401]);

    await expectLater(
      client(adapter).get<dynamic>('/api/thing'),
      throwsA(
        isA<DioException>().having(
          (e) => e.response?.statusCode,
          'status',
          401,
        ),
      ),
    );
    expect(adapter.authorizationHeaders, hasLength(2));
    expect(sessionExpiredCalls, 1);
  });

  test('an expired session never sends the request', () async {
    tokenError = const ApiFailure(
      code: ApiFailure.sessionExpired,
      message: 'expired',
      retryable: false,
    );
    final adapter = ScriptedAdapter([200]);

    final error = await client(adapter)
        .get<dynamic>('/api/thing')
        .then<DioException?>(
          (_) => null,
          onError: (Object e) => e as DioException,
        );

    expect(adapter.authorizationHeaders, isEmpty);
    expect(sessionExpiredCalls, 1);
    expect(ApiFailure.fromDioException(error!).code, ApiFailure.sessionExpired);
  });

  test('a transient token failure keeps the user signed in', () async {
    tokenError = const ApiFailure(
      code: ApiFailure.networkUnavailable,
      message: 'offline',
      retryable: true,
    );
    final adapter = ScriptedAdapter([200]);

    final error = await client(adapter)
        .get<dynamic>('/api/thing')
        .then<DioException?>(
          (_) => null,
          onError: (Object e) => e as DioException,
        );

    expect(sessionExpiredCalls, 0);
    final failure = ApiFailure.fromDioException(error!);
    expect(failure.code, ApiFailure.networkUnavailable);
    expect(failure.retryable, isTrue);
  });
}
