/// Route paths live in one place so navigation calls never use string literals.
/// Kept separate from the router so feature screens can import it without a cycle.
abstract final class AppRoutes {
  static const splash = '/splash';
  static const login = '/login';
  static const home = '/';
  static const scan = '/scan';
  static const preview = '/contact-preview';
  static const saved = '/saved';
}
