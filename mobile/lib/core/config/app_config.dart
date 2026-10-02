import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

/// Build-time configuration supplied with `--dart-define-from-file`.
///
/// Nothing secret belongs here: a mobile binary can always be decompiled.
/// Entra tenant/client IDs, scopes and the API URL are public identifiers.
class AppConfig {
  const AppConfig({
    required this.apiBaseUrl,
    required this.entraTenantId,
    required this.entraClientId,
    required this.entraApiScope,
    required this.entraRedirectUri,
    this.isRelease = kReleaseMode,
  });

  factory AppConfig.fromEnvironment() {
    return const AppConfig(
      apiBaseUrl: String.fromEnvironment(
        'API_BASE_URL',
        defaultValue: 'http://10.0.2.2:5115',
      ),
      entraTenantId: String.fromEnvironment('ENTRA_TENANT_ID'),
      entraClientId: String.fromEnvironment('ENTRA_CLIENT_ID'),
      entraApiScope: String.fromEnvironment('ENTRA_API_SCOPE'),
      entraRedirectUri: String.fromEnvironment(
        'ENTRA_REDIRECT_URI',
        defaultValue: 'com.example.businesscardscanner://oauthredirect',
      ),
    );
  }

  /// Base URL of BusinessCard.Api. The default is the Android emulator's
  /// alias for the host machine, for local development only.
  final String apiBaseUrl;

  /// Directory (tenant) ID. Single-tenant sign-in only.
  final String entraTenantId;

  /// Client ID of the *mobile* app registration (not the API's).
  final String entraClientId;

  /// Full API scope, e.g. `api://<api-client-id>/access_as_user`.
  final String entraApiScope;

  /// Must match the redirect URI registered in Entra and the platform
  /// URL scheme (Android manifest placeholder / iOS CFBundleURLSchemes).
  final String entraRedirectUri;

  final bool isRelease;

  /// Human-readable configuration problems; empty when the app can start.
  List<String> get problems => [
    if (entraTenantId.isEmpty) 'ENTRA_TENANT_ID is not set.',
    if (entraClientId.isEmpty) 'ENTRA_CLIENT_ID is not set.',
    if (entraApiScope.isEmpty) 'ENTRA_API_SCOPE is not set.',
    if (Uri.tryParse(apiBaseUrl)?.hasAuthority != true)
      'API_BASE_URL is not a valid URL.',
    if (isRelease && !apiBaseUrl.startsWith('https://'))
      'API_BASE_URL must use https:// in release builds.',
  ];
}

final appConfigProvider = Provider<AppConfig>(
  (ref) => AppConfig.fromEnvironment(),
);
