import 'package:business_card_scanner/features/authentication/data/auth_service.dart';
import 'package:business_card_scanner/features/authentication/domain/auth_exceptions.dart';
import 'package:business_card_scanner/features/authentication/domain/auth_state.dart';
import 'package:business_card_scanner/features/authentication/presentation/auth_controller.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../support/auth_fakes.dart';

void main() {
  late FakeAuthService auth;
  late ProviderContainer container;

  setUp(() {
    auth = FakeAuthService();
    container = ProviderContainer(
      overrides: [authServiceProvider.overrideWithValue(auth)],
    );
    addTearDown(container.dispose);
  });

  AuthController controller() =>
      container.read(authControllerProvider.notifier);
  AuthState state() => container.read(authControllerProvider);

  Future<void> settle() => Future<void>.delayed(Duration.zero);

  test('starts restoring, then signs out when no session is stored', () async {
    expect(state(), isA<AuthRestoring>());
    await settle();
    expect(state(), isA<SignedOut>());
  });

  test('restores a stored session', () async {
    auth.restoredAccount = testAccount;
    state();
    await settle();

    expect((state() as SignedIn).account.username, testAccount.username);
  });

  test('signIn moves through SigningIn to SignedIn', () async {
    state();
    await settle();

    final future = controller().signIn();
    expect(state(), isA<SigningIn>());
    await future;

    expect(state(), isA<SignedIn>());
  });

  test('cancelled sign-in returns to SignedOut without a message', () async {
    auth.signInError = const SignInCancelledException();
    state();
    await settle();

    await controller().signIn();

    expect((state() as SignedOut).message, isNull);
  });

  test('failed sign-in shows the failure message', () async {
    auth.signInError = const AuthFailureException('Sign-in failed.');
    state();
    await settle();

    await controller().signIn();

    expect((state() as SignedOut).message, 'Sign-in failed.');
  });

  test('signOut clears the session', () async {
    auth.restoredAccount = testAccount;
    state();
    await settle();

    await controller().signOut();

    expect(state(), isA<SignedOut>());
    expect(auth.signOutCalls, 1);
  });

  test('session expiry signs out with an explanation, once', () async {
    auth.restoredAccount = testAccount;
    state();
    await settle();

    await controller().handleSessionExpired();
    await controller().handleSessionExpired();

    expect(
      (state() as SignedOut).message,
      AuthController.sessionExpiredMessage,
    );
    expect(auth.signOutCalls, 1);
  });
}
