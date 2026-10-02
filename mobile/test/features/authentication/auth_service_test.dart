import 'dart:async';

import 'package:business_card_scanner/features/authentication/data/auth_service.dart';
import 'package:business_card_scanner/features/authentication/domain/auth_exceptions.dart';
import 'package:flutter/services.dart';
import 'package:flutter_appauth/flutter_appauth.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../support/auth_fakes.dart';

void main() {
  late FakeAppAuth appAuth;
  late InMemoryTokenStore store;
  late DateTime now;
  late AuthService service;

  TokenResponse tokenResponse({
    String access = 'access-2',
    String? refresh = 'refresh-2',
    Duration lifetime = const Duration(hours: 1),
  }) => TokenResponse(
    access,
    refresh,
    now.add(lifetime),
    null,
    'Bearer',
    null,
    null,
  );

  setUp(() {
    appAuth = FakeAppAuth();
    store = InMemoryTokenStore();
    now = DateTime.utc(2026, 10, 2, 9);
    service = AuthService(
      appAuth: appAuth,
      tokenStore: store,
      config: testConfig,
      clock: () => now,
    );
  });

  group('signIn', () {
    test('requests API scope with refresh token and account picker', () async {
      appAuth.onAuthorize = (_) async => AuthorizationTokenResponse(
        'access-1',
        'refresh-1',
        now.add(const Duration(hours: 1)),
        idTokenFor(testAccount),
        'Bearer',
        null,
        null,
        null,
      );

      final account = await service.signIn();

      final request = appAuth.lastAuthorizeRequest!;
      expect(
        request.scopes,
        containsAll(['offline_access', testConfig.entraApiScope]),
      );
      expect(request.promptValues, [Prompt.selectAccount]);
      expect(
        request.serviceConfiguration!.tokenEndpoint,
        'https://login.microsoftonline.com/tenant-id/oauth2/v2.0/token',
      );
      expect(account.displayName, testAccount.displayName);
      expect(store.refreshToken, 'refresh-1');
      expect(store.account?.username, testAccount.username);
      expect(await service.getAccessToken(), 'access-1');
      expect(appAuth.tokenCalls, 0);
    });

    test('maps a closed browser to SignInCancelledException', () async {
      appAuth.onAuthorize = (_) => Future.error(
        FlutterAppAuthUserCancelledException(
          code: 'user_cancelled',
          platformErrorDetails: FlutterAppAuthPlatformErrorDetails(),
        ),
      );

      await expectLater(
        service.signIn(),
        throwsA(isA<SignInCancelledException>()),
      );
      expect(store.refreshToken, isNull);
    });

    test(
      'maps platform errors to a displayable AuthFailureException',
      () async {
        appAuth.onAuthorize = (_) =>
            Future.error(PlatformException(code: 'network'));

        await expectLater(
          service.signIn(),
          throwsA(isA<AuthFailureException>()),
        );
      },
    );
  });

  group('getAccessToken', () {
    setUp(() => store.refreshToken = 'refresh-1');

    test(
      'reuses the cached token while it is outside the expiry buffer',
      () async {
        appAuth.onToken = (_) async => tokenResponse(access: 'access-1');
        await service.getAccessToken();

        now = now.add(const Duration(minutes: 30));
        expect(await service.getAccessToken(), 'access-1');
        expect(appAuth.tokenCalls, 1);
      },
    );

    test('refreshes inside the expiry buffer and persists the rotated refresh token', () async {
      appAuth.onToken = (_) async =>
          tokenResponse(access: 'access-1', refresh: 'refresh-1');
      await service.getAccessToken();

      now = now.add(const Duration(minutes: 59));
      appAuth.onToken = (_) async =>
          tokenResponse(access: 'access-2', refresh: 'refresh-2');

      expect(await service.getAccessToken(), 'access-2');
      expect(appAuth.lastTokenRequest!.refreshToken, 'refresh-1');
      expect(store.refreshToken, 'refresh-2');
    });

    test('concurrent callers share one refresh request', () async {
      final pending = Completer<TokenResponse>();
      appAuth.onToken = (_) => pending.future;

      final calls = [
        service.getAccessToken(),
        service.getAccessToken(),
        service.getAccessToken(),
      ];
      pending.complete(tokenResponse(access: 'shared'));

      expect(await Future.wait(calls), ['shared', 'shared', 'shared']);
      expect(appAuth.tokenCalls, 1);
    });

    test('forceRefresh bypasses a valid cached token', () async {
      appAuth.onToken = (_) async => tokenResponse(access: 'access-1');
      await service.getAccessToken();
      appAuth.onToken = (_) async => tokenResponse(access: 'access-forced');

      expect(await service.getAccessToken(forceRefresh: true), 'access-forced');
    });

    for (final error in ['invalid_grant', 'interaction_required']) {
      test(
        '$error clears the session and throws SessionExpiredException',
        () async {
          store.account = testAccount;
          appAuth.onToken = (_) => Future.error(oauthError(error));

          await expectLater(
            service.getAccessToken(),
            throwsA(isA<SessionExpiredException>()),
          );
          expect(store.refreshToken, isNull);
          expect(store.account, isNull);
        },
      );
    }

    test(
      'network failure keeps the session and throws AuthFailureException',
      () async {
        appAuth.onToken = (_) =>
            Future.error(PlatformException(code: 'network'));

        await expectLater(
          service.getAccessToken(),
          throwsA(isA<AuthFailureException>()),
        );
        expect(store.refreshToken, 'refresh-1');
      },
    );

    test('a failed refresh does not block the next attempt', () async {
      appAuth.onToken = (_) => Future.error(PlatformException(code: 'network'));
      await expectLater(
        service.getAccessToken(),
        throwsA(isA<AuthFailureException>()),
      );

      appAuth.onToken = (_) async => tokenResponse(access: 'recovered');
      expect(await service.getAccessToken(), 'recovered');
    });

    test(
      'without a stored refresh token throws SessionExpiredException',
      () async {
        store.refreshToken = null;

        await expectLater(
          service.getAccessToken(),
          throwsA(isA<SessionExpiredException>()),
        );
        expect(appAuth.tokenCalls, 0);
      },
    );
  });

  group('restoreSession', () {
    test('returns null when nothing is stored', () async {
      expect(await service.restoreSession(), isNull);
    });

    test('returns the account after a successful silent refresh', () async {
      store
        ..refreshToken = 'refresh-1'
        ..account = testAccount;
      appAuth.onToken = (_) async => tokenResponse();

      expect((await service.restoreSession())?.username, testAccount.username);
    });

    test('stays signed in when offline', () async {
      store
        ..refreshToken = 'refresh-1'
        ..account = testAccount;
      appAuth.onToken = (_) => Future.error(PlatformException(code: 'network'));

      expect(await service.restoreSession(), isNotNull);
      expect(store.refreshToken, 'refresh-1');
    });

    test('returns null when the session was revoked', () async {
      store
        ..refreshToken = 'refresh-1'
        ..account = testAccount;
      appAuth.onToken = (_) => Future.error(oauthError('invalid_grant'));

      expect(await service.restoreSession(), isNull);
    });
  });

  test('signOut forgets the cached access token and stored session', () async {
    store
      ..refreshToken = 'refresh-1'
      ..account = testAccount;
    appAuth.onToken = (_) async => tokenResponse();
    await service.getAccessToken();

    await service.signOut();

    expect(store.refreshToken, isNull);
    await expectLater(
      service.getAccessToken(),
      throwsA(isA<SessionExpiredException>()),
    );
  });
}
