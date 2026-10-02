import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../app/routes.dart';
import 'save_contact_controller.dart';

class SavedScreen extends ConsumerWidget {
  const SavedScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final state = ref.watch(saveContactControllerProvider);
    final name = state is Saved ? state.contact.displayName : null;
    final updated = state is Saved && state.updatedExisting;
    final theme = Theme.of(context);

    void done() {
      ref.read(saveContactControllerProvider.notifier).reset();
      context.go(AppRoutes.home);
    }

    return PopScope(
      canPop: false,
      onPopInvokedWithResult: (didPop, _) {
        if (!didPop) {
          done();
        }
      },
      child: Scaffold(
        body: SafeArea(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Icon(
                  Icons.check_circle,
                  size: 80,
                  color: theme.colorScheme.primary,
                ),
                const SizedBox(height: 24),
                Text(
                  SaveContactController.successMessage,
                  style: theme.textTheme.headlineSmall,
                  textAlign: TextAlign.center,
                ),
                if (updated) ...[
                  const SizedBox(height: 8),
                  Text(
                    'The existing contact was updated.',
                    style: theme.textTheme.bodyMedium,
                    textAlign: TextAlign.center,
                  ),
                ],
                if (name != null && name.isNotEmpty) ...[
                  const SizedBox(height: 8),
                  Text(
                    name,
                    style: theme.textTheme.titleMedium,
                    textAlign: TextAlign.center,
                  ),
                ],
                const SizedBox(height: 32),
                FilledButton(
                  onPressed: done,
                  child: const Text('Scan another card'),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
