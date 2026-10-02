/// The user closed the sign-in browser. Not an error worth showing.
class SignInCancelledException implements Exception {
  const SignInCancelledException();
}

/// The refresh token is no longer usable (revoked, expired, password reset,
/// new Conditional Access requirement). The user must sign in interactively.
class SessionExpiredException implements Exception {
  const SessionExpiredException();
}

/// Sign-in or token acquisition failed for another reason (network, config).
class AuthFailureException implements Exception {
  const AuthFailureException(this.message);

  /// Safe to display.
  final String message;

  @override
  String toString() => 'AuthFailureException: $message';
}
