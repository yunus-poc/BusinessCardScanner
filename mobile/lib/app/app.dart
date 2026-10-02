import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/config/app_config.dart';
import '../core/theme/app_theme.dart';
import 'config_error_screen.dart';
import 'router.dart';

class BusinessCardApp extends ConsumerWidget {
  const BusinessCardApp({super.key});

  static const title = 'Business Card Scanner';

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final problems = ref.watch(appConfigProvider).problems;
    if (problems.isNotEmpty) {
      // A build without Entra settings cannot sign in; say so instead of failing later.
      return MaterialApp(
        title: title,
        theme: AppTheme.light(),
        home: ConfigErrorScreen(problems: problems),
      );
    }

    return MaterialApp.router(
      title: title,
      theme: AppTheme.light(),
      darkTheme: AppTheme.dark(),
      routerConfig: ref.watch(routerProvider),
      debugShowCheckedModeBanner: false,
    );
  }
}
