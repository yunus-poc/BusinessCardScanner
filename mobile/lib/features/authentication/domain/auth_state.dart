import 'auth_account.dart';

sealed class AuthState {
  const AuthState();
}

/// App start: checking secure storage for an existing session.
final class AuthRestoring extends AuthState {
  const AuthRestoring();
}

final class SignedOut extends AuthState {
  const SignedOut({this.message});

  /// Shown on the login screen, e.g. after a session expired.
  final String? message;
}

final class SigningIn extends AuthState {
  const SigningIn();
}

final class SignedIn extends AuthState {
  const SignedIn(this.account);

  final AuthAccount account;
}
