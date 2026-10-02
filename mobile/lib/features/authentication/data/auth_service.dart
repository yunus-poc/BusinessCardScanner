import 'dart:async';

import 'package:flutter/services.dart';
import 'package:flutter_appauth/flutter_appauth.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/config/app_config.dart';
import '../domain/auth_account.dart';
import '../domain/auth_exceptions.dart';
import 'token_store.dart';

/// Entra ID sign-in (authorization code + PKCE via the system browser) and
/// access-token lifecycle for the Business Card API.
///
/// This is the only class that knows about AppAuth. If Conditional Access
/// requires a broker, replace its internals with an MSAL plugin; callers
/// (controller, HTTP client) only see the public methods.
class AuthService {
  AuthService({
    required this._appAuth,
    required TokenStore tokenStore,
    required AppConfig config,
    DateTime Function()? clock,
  }) : _store = tokenStore,
       _clientId = config.entraClientId,
       _redirectUri = config.entraRedirectUri,
       _apiScope = config.entraApiScope,
       _clock = clock ?? DateTime.now,
       // Explicit endpoints skip a discovery-document round trip on every sign-in.
       _endpoints = AuthorizationServiceConfiguration(
         authorizationEndpoint:
             'https://login.microsoftonline.com/${config.entraTenantId}/oauth2/v2.0/authorize',
         tokenEndpoint:
             'https://login.microsoftonline.com/${config.entraTenantId}/oauth2/v2.0/token',
       );

  /// Refresh a little early so a token never expires mid-request.
  static const expiryBuffer = Duration(minutes: 2);

  /// Used when the token response carries no expiry (should not happen with Entra).
  static const _assumedLifetime = Duration(minutes: 10);

  /// OAuth errors meaning "the refresh token can no longer be used silently".
  static const _sessionEndedErrors = {
    FlutterAppAuthOAuthError.invalidGrant,
    'interaction_required',
    'login_required',
    'consent_required',
  };

  final FlutterAppAuth _appAuth;
  final TokenStore _store;
  final String _clientId;
  final String _redirectUri;
  final String _apiScope;
  final DateTime Function() _clock;
  final AuthorizationServiceConfiguration _endpoints;

  String? _accessToken;
  DateTime? _accessTokenExpiry;
  Future<String>? _refreshInFlight;

  // One resource per Entra token: our API. offline_access yields a refresh token.
  List<String> get _scopes => [
    'openid',
    'profile',
    'offline_access',
    _apiScope,
  ];

  /// Interactive sign-in. Always shows the account picker so a shared device
  /// never silently continues as the previous user.
  Future<AuthAccount> signIn() async {
    final AuthorizationTokenResponse response;
    try {
      response = await _appAuth.authorizeAndExchangeCode(
        AuthorizationTokenRequest(
          _clientId,
          _redirectUri,
          serviceConfiguration: _endpoints,
          scopes: _scopes,
          promptValues: const [Prompt.selectAccount],
        ),
      );
    } on FlutterAppAuthUserCancelledException {
      throw const SignInCancelledException();
    } on PlatformException {
      throw const AuthFailureException(
        'Sign-in failed. Check your connection and try again.',
      );
    }

    final refreshToken = response.refreshToken;
    if (refreshToken == null || response.accessToken == null) {
      throw const AuthFailureException(
        'Sign-in did not return a usable session.',
      );
    }

    final account = AuthAccount.fromIdToken(response.idToken);
    _cacheAccessToken(
      response.accessToken!,
      response.accessTokenExpirationDateTime,
    );
    await _store.saveRefreshToken(refreshToken);
    await _store.saveAccount(account);
    return account;
  }

  /// Returns the stored account if a session exists and is still usable.
  ///
  /// Validates the session with one silent refresh. If that fails only because
  /// the device is offline, the user stays signed in and the next API call
  /// retries; only a definitive "session ended" answer signs the user out.
  Future<AuthAccount?> restoreSession() async {
    final account = await _store.readAccount();
    if (account == null || await _store.readRefreshToken() == null) {
      return null;
    }
    try {
      await getAccessToken(forceRefresh: true);
    } on SessionExpiredException {
      return null;
    } on AuthFailureException {
      // Transient (offline, timeout): keep the session.
    }
    return account;
  }

  /// A valid access token for the API, refreshing silently when needed.
  /// Concurrent callers share a single refresh request.
  Future<String> getAccessToken({bool forceRefresh = false}) {
    final cached = _accessToken;
    final expiry = _accessTokenExpiry;
    if (!forceRefresh &&
        cached != null &&
        expiry != null &&
        _clock().isBefore(expiry.subtract(expiryBuffer))) {
      return Future.value(cached);
    }
    return _refreshInFlight ??= _refresh().whenComplete(
      () => _refreshInFlight = null,
    );
  }

  /// Local sign-out: forget all tokens on this device.
  Future<void> signOut() async {
    _accessToken = null;
    _accessTokenExpiry = null;
    await _store.clear();
  }

  Future<String> _refresh() async {
    final refreshToken = await _store.readRefreshToken();
    if (refreshToken == null) {
      throw const SessionExpiredException();
    }

    final TokenResponse response;
    try {
      response = await _appAuth.token(
        TokenRequest(
          _clientId,
          _redirectUri,
          serviceConfiguration: _endpoints,
          refreshToken: refreshToken,
          scopes: _scopes,
        ),
      );
    } on FlutterAppAuthPlatformException catch (e) {
      if (_sessionEndedErrors.contains(e.platformErrorDetails.error)) {
        await signOut();
        throw const SessionExpiredException();
      }
      throw const AuthFailureException(
        'Could not refresh your session. Check your connection.',
      );
    } on PlatformException {
      throw const AuthFailureException(
        'Could not refresh your session. Check your connection.',
      );
    }

    final accessToken = response.accessToken;
    if (accessToken == null) {
      throw const AuthFailureException(
        'Token refresh returned no access token.',
      );
    }
    _cacheAccessToken(accessToken, response.accessTokenExpirationDateTime);

    // Entra rotates refresh tokens; the old one keeps working for a while,
    // but always persist the newest.
    final rotated = response.refreshToken;
    if (rotated != null && rotated != refreshToken) {
      await _store.saveRefreshToken(rotated);
    }
    return accessToken;
  }

  void _cacheAccessToken(String token, DateTime? expiry) {
    _accessToken = token;
    _accessTokenExpiry = expiry ?? _clock().add(_assumedLifetime);
  }
}

final authServiceProvider = Provider<AuthService>((ref) {
  return AuthService(
    appAuth: const FlutterAppAuth(),
    tokenStore: TokenStore(),
    config: ref.watch(appConfigProvider),
  );
});
