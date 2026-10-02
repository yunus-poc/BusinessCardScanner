import 'package:business_card_scanner/core/config/app_config.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  AppConfig config({
    String apiBaseUrl = 'https://api.test',
    required bool isRelease,
  }) => AppConfig(
    apiBaseUrl: apiBaseUrl,
    entraTenantId: 'tenant',
    entraClientId: 'client',
    entraApiScope: 'api://x/access_as_user',
    entraRedirectUri: 'com.example.businesscardscanner://oauthredirect',
    isRelease: isRelease,
  );

  test('complete configuration has no problems', () {
    expect(config(isRelease: true).problems, isEmpty);
  });

  test('plain HTTP is allowed in debug but rejected in release', () {
    expect(
      config(apiBaseUrl: 'http://10.0.2.2:5115', isRelease: false).problems,
      isEmpty,
    );
    expect(
      config(apiBaseUrl: 'http://api.internal', isRelease: true).problems,
      contains('API_BASE_URL must use https:// in release builds.'),
    );
  });

  test('invalid base URL is reported', () {
    expect(
      config(apiBaseUrl: 'not a url', isRelease: false).problems,
      contains('API_BASE_URL is not a valid URL.'),
    );
  });
}
