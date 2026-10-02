import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../data/auth_service.dart';
import '../domain/auth_exceptions.dart';
import '../domain/auth_state.dart';

class AuthController extends Notifier<AuthState> {
  static const sessionExpiredMessage =
      'Your session has expired. Please sign in again.';

  AuthService get _service => ref.read(authServiceProvider);

  @override
  AuthState build() {
    unawaited(Future.microtask(_restore));
    return const AuthRestoring();
  }

  Future<void> signIn() async {
    if (state is SigningIn) {
      return;
    }
    state = const SigningIn();
    try {
      final account = await _service.signIn();
      _set(SignedIn(account));
    } on SignInCancelledException {
      _set(const SignedOut());
    } on AuthFailureException catch (e) {
      _set(SignedOut(message: e.message));
    }
  }

  Future<void> signOut() async {
    await _service.signOut();
    _set(const SignedOut());
  }

  /// Called by the HTTP layer when the API rejects the session for good.
  Future<void> handleSessionExpired() async {
    if (state is! SignedIn) {
      return;
    }
    await _service.signOut();
    _set(const SignedOut(message: sessionExpiredMessage));
  }

  Future<void> _restore() async {
    final account = await _service.restoreSession();
    _set(account == null ? const SignedOut() : SignedIn(account));
  }

  void _set(AuthState next) {
    if (ref.mounted) {
      state = next;
    }
  }
}

final authControllerProvider = NotifierProvider<AuthController, AuthState>(
  AuthController.new,
);
