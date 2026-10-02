import 'dart:convert';

import 'package:business_card_scanner/core/config/app_config.dart';
import 'package:business_card_scanner/features/authentication/data/auth_service.dart';
import 'package:business_card_scanner/features/authentication/data/token_store.dart';
import 'package:business_card_scanner/features/authentication/domain/auth_account.dart';
import 'package:flutter_appauth/flutter_appauth.dart';
import 'package:flutter_test/flutter_test.dart';

const testConfig = AppConfig(
  apiBaseUrl: 'https://api.test',
  entraTenantId: 'tenant-id',
  entraClientId: 'mobile-client-id',
  entraApiScope: 'api://api-client-id/access_as_user',
  entraRedirectUri: 'com.example.businesscardscanner://oauthredirect',
  isRelease: false,
);

const testAccount = AuthAccount(
  displayName: 'Ayşe Demir',
  username: 'ayse.demir@contoso.test',
);

/// Unsigned JWT with display claims; AuthAccount only reads the payload.
String idTokenFor(AuthAccount account) {
  String part(Map<String, Object> json) =>
      base64Url.encode(utf8.encode(jsonEncode(json))).replaceAll('=', '');
  return '${part({'alg': 'none'})}.'
      '${part({'name': account.displayName, 'preferred_username': account.username})}.sig';
}

FlutterAppAuthPlatformException oauthError(String error) =>
    FlutterAppAuthPlatformException(
      code: 'token_failed',
      platformErrorDetails: FlutterAppAuthPlatformErrorDetails(error: error),
    );

class FakeAppAuth extends Fake implements FlutterAppAuth {
  Future<AuthorizationTokenResponse> Function(AuthorizationTokenRequest)?
  onAuthorize;
  Future<TokenResponse> Function(TokenRequest)? onToken;

  int authorizeCalls = 0;
  int tokenCalls = 0;
  TokenRequest? lastTokenRequest;
  AuthorizationTokenRequest? lastAuthorizeRequest;

  @override
  Future<AuthorizationTokenResponse> authorizeAndExchangeCode(
    AuthorizationTokenRequest request,
  ) {
    authorizeCalls++;
    lastAuthorizeRequest = request;
    return onAuthorize!(request);
  }

  @override
  Future<TokenResponse> token(TokenRequest request) {
    tokenCalls++;
    lastTokenRequest = request;
    return onToken!(request);
  }
}

class InMemoryTokenStore implements TokenStore {
  String? refreshToken;
  AuthAccount? account;

  @override
  Future<String?> readRefreshToken() async => refreshToken;

  @override
  Future<AuthAccount?> readAccount() async => account;

  @override
  Future<void> saveRefreshToken(String value) async => refreshToken = value;

  @override
  Future<void> saveAccount(AuthAccount value) async => account = value;

  @override
  Future<void> clear() async {
    refreshToken = null;
    account = null;
  }
}

/// Scriptable AuthService for controller/router/widget tests.
class FakeAuthService implements AuthService {
  AuthAccount? restoredAccount;
  Object? signInError;
  int signOutCalls = 0;

  @override
  Future<AuthAccount?> restoreSession() async => restoredAccount;

  @override
  Future<AuthAccount> signIn() async {
    if (signInError case final error?) {
      throw error;
    }
    return testAccount;
  }

  @override
  Future<String> getAccessToken({bool forceRefresh = false}) async =>
      'access-token';

  @override
  Future<void> signOut() async => signOutCalls++;
}
