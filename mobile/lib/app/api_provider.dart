import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/api/api_client.dart';
import '../core/config/app_config.dart';
import '../core/errors/api_failure.dart';
import '../features/authentication/data/auth_service.dart';
import '../features/authentication/domain/auth_exceptions.dart';
import '../features/authentication/presentation/auth_controller.dart';

/// Wires the feature-agnostic HTTP client to authentication. Lives in `app/`
/// so `core/` never imports a feature.
final apiClientProvider = Provider<Dio>((ref) {
  final auth = ref.watch(authServiceProvider);
  final dio = createApiClient(
    baseUrl: ref.watch(appConfigProvider).apiBaseUrl,
    accessToken: ({forceRefresh = false}) async {
      try {
        return await auth.getAccessToken(forceRefresh: forceRefresh);
      } on SessionExpiredException {
        throw const ApiFailure(
          code: ApiFailure.sessionExpired,
          message: AuthController.sessionExpiredMessage,
          retryable: false,
        );
      } on AuthFailureException catch (e) {
        throw ApiFailure(
          code: ApiFailure.networkUnavailable,
          message: e.message,
          retryable: true,
        );
      }
    },
    onSessionExpired: () =>
        ref.read(authControllerProvider.notifier).handleSessionExpired(),
  );
  ref.onDispose(dio.close);
  return dio;
});
