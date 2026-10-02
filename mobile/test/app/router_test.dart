import 'package:business_card_scanner/app/app.dart';
import 'package:business_card_scanner/app/router.dart';
import 'package:business_card_scanner/core/config/app_config.dart';
import 'package:business_card_scanner/features/authentication/data/auth_service.dart';
import 'package:business_card_scanner/features/authentication/domain/auth_state.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

import '../support/auth_fakes.dart';

void main() {
  group('authRedirect', () {
    test('restoring keeps the user on the splash screen', () {
      expect(
        authRedirect(const AuthRestoring(), AppRoutes.home),
        AppRoutes.splash,
      );
      expect(authRedirect(const AuthRestoring(), AppRoutes.splash), isNull);
    });

    test('signed-out users can only reach login', () {
      expect(authRedirect(const SignedOut(), AppRoutes.home), AppRoutes.login);
      expect(
        authRedirect(const SigningIn(), AppRoutes.splash),
        AppRoutes.login,
      );
      expect(authRedirect(const SignedOut(), AppRoutes.login), isNull);
    });

    test('signed-in users leave auth pages and stay elsewhere', () {
      const signedIn = SignedIn(testAccount);
      expect(authRedirect(signedIn, AppRoutes.login), AppRoutes.home);
      expect(authRedirect(signedIn, AppRoutes.splash), AppRoutes.home);
      expect(authRedirect(signedIn, AppRoutes.home), isNull);
    });
  });

  group('app', () {
    Widget app(FakeAuthService auth, {AppConfig config = testConfig}) =>
        ProviderScope(
          overrides: [
            appConfigProvider.overrideWithValue(config),
            authServiceProvider.overrideWithValue(auth),
          ],
          child: const BusinessCardApp(),
        );

    testWidgets('shows login when no session is stored', (tester) async {
      await tester.pumpWidget(app(FakeAuthService()));
      await tester.pumpAndSettle();

      expect(find.text('Sign in with Microsoft'), findsOneWidget);
    });

    testWidgets('signing in lands on home; signing out returns to login', (
      tester,
    ) async {
      await tester.pumpWidget(app(FakeAuthService()));
      await tester.pumpAndSettle();

      await tester.tap(find.text('Sign in with Microsoft'));
      await tester.pumpAndSettle();
      expect(find.text('Scan Business Card'), findsOneWidget);

      await tester.tap(find.byTooltip('Account'));
      await tester.pumpAndSettle();
      expect(find.text(testAccount.displayName), findsOneWidget);
      await tester.tap(find.text('Sign out'));
      await tester.pumpAndSettle();

      expect(find.text('Sign in with Microsoft'), findsOneWidget);
    });

    testWidgets('restored session goes straight to home', (tester) async {
      await tester.pumpWidget(
        app(FakeAuthService()..restoredAccount = testAccount),
      );
      await tester.pumpAndSettle();

      expect(find.text('Scan Business Card'), findsOneWidget);
    });

    testWidgets('missing Entra settings show the configuration screen', (
      tester,
    ) async {
      const incomplete = AppConfig(
        apiBaseUrl: 'https://api.test',
        entraTenantId: '',
        entraClientId: '',
        entraApiScope: '',
        entraRedirectUri: 'x://y',
        isRelease: false,
      );

      await tester.pumpWidget(app(FakeAuthService(), config: incomplete));
      await tester.pumpAndSettle();

      expect(find.text('Configuration required'), findsOneWidget);
      expect(find.text('ENTRA_CLIENT_ID is not set.'), findsOneWidget);
    });
  });
}
