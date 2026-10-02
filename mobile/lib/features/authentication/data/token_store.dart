import 'dart:convert';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../domain/auth_account.dart';

/// Persists the refresh token and display account in platform secure storage
/// (iOS Keychain, Android Keystore-backed encryption).
///
/// The access token is deliberately *not* persisted: it is short-lived and
/// kept in memory only, so less sensitive material rests on the device.
class TokenStore {
  TokenStore([FlutterSecureStorage? storage])
    : _storage =
          storage ??
          const FlutterSecureStorage(
            // Readable after first unlock (background refresh), never synced
            // to iCloud or migrated to another device.
            iOptions: IOSOptions(
              accessibility: KeychainAccessibility.first_unlock_this_device,
            ),
          );

  static const _refreshTokenKey = 'auth.refresh_token';
  static const _accountKey = 'auth.account';

  final FlutterSecureStorage _storage;

  Future<String?> readRefreshToken() => _storage.read(key: _refreshTokenKey);

  Future<AuthAccount?> readAccount() async {
    final json = await _storage.read(key: _accountKey);
    if (json == null) {
      return null;
    }
    return AuthAccount.fromJson(jsonDecode(json) as Map<String, dynamic>);
  }

  Future<void> saveRefreshToken(String refreshToken) =>
      _storage.write(key: _refreshTokenKey, value: refreshToken);

  Future<void> saveAccount(AuthAccount account) =>
      _storage.write(key: _accountKey, value: jsonEncode(account.toJson()));

  Future<void> clear() async {
    await _storage.delete(key: _refreshTokenKey);
    await _storage.delete(key: _accountKey);
  }
}
