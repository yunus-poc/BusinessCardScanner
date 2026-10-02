import 'dart:convert';

/// The signed-in user as shown in the UI. Display data only, never used for
/// authorization decisions (the API validates the access token itself).
class AuthAccount {
  const AuthAccount({required this.displayName, required this.username});

  /// Reads display claims from an ID token received directly from Entra's
  /// token endpoint over TLS. The signature is not checked because nothing
  /// security-relevant depends on these values.
  factory AuthAccount.fromIdToken(String? idToken) {
    final claims = _decodePayload(idToken);
    final username = claims['preferred_username'] as String? ?? '';
    return AuthAccount(
      displayName: claims['name'] as String? ?? username,
      username: username,
    );
  }

  factory AuthAccount.fromJson(Map<String, dynamic> json) => AuthAccount(
    displayName: json['displayName'] as String? ?? '',
    username: json['username'] as String? ?? '',
  );

  final String displayName;
  final String username;

  Map<String, dynamic> toJson() => {
    'displayName': displayName,
    'username': username,
  };

  static Map<String, dynamic> _decodePayload(String? jwt) {
    final parts = jwt?.split('.') ?? const <String>[];
    if (parts.length != 3) {
      return const {};
    }
    try {
      final payload = utf8.decode(
        base64Url.decode(base64Url.normalize(parts[1])),
      );
      return jsonDecode(payload) as Map<String, dynamic>;
    } on FormatException {
      return const {};
    }
  }
}
