import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../features/authentication/domain/auth_state.dart';
import '../features/authentication/presentation/auth_controller.dart';
import '../features/authentication/presentation/login_screen.dart';
import '../features/authentication/presentation/splash_screen.dart';
import '../features/contact_preview/presentation/contact_preview_screen.dart';
import '../features/contacts/presentation/saved_screen.dart';
import '../features/home/home_screen.dart';
import '../features/scan_card/presentation/scan_screen.dart';
import 'routes.dart';

export 'routes.dart';

/// Pure routing rule, unit-testable without widgets: the auth state alone
/// decides which area of the app is reachable.
String? authRedirect(AuthState auth, String location) {
  final onAuthPage =
      location == AppRoutes.login || location == AppRoutes.splash;
  return switch (auth) {
    AuthRestoring() => location == AppRoutes.splash ? null : AppRoutes.splash,
    SignedOut() ||
    SigningIn() => location == AppRoutes.login ? null : AppRoutes.login,
    SignedIn() => onAuthPage ? AppRoutes.home : null,
  };
}

final routerProvider = Provider<GoRouter>((ref) {
  final authState = ValueNotifier<AuthState>(ref.read(authControllerProvider));
  ref.listen(authControllerProvider, (_, next) => authState.value = next);

  final router = GoRouter(
    initialLocation: AppRoutes.splash,
    refreshListenable: authState,
    redirect: (context, state) =>
        authRedirect(authState.value, state.matchedLocation),
    routes: [
      GoRoute(
        path: AppRoutes.splash,
        builder: (context, state) => const SplashScreen(),
      ),
      GoRoute(
        path: AppRoutes.login,
        builder: (context, state) => const LoginScreen(),
      ),
      GoRoute(
        path: AppRoutes.home,
        builder: (context, state) => const HomeScreen(),
      ),
      GoRoute(
        path: AppRoutes.scan,
        builder: (context, state) => const ScanScreen(),
      ),
      GoRoute(
        path: AppRoutes.preview,
        builder: (context, state) => const ContactPreviewScreen(),
      ),
      GoRoute(
        path: AppRoutes.saved,
        builder: (context, state) => const SavedScreen(),
      ),
    ],
  );

  ref.onDispose(() {
    router.dispose();
    authState.dispose();
  });
  return router;
});
