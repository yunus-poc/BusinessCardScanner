import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../app/routes.dart';
import '../authentication/domain/auth_state.dart';
import '../authentication/presentation/auth_controller.dart';
import '../scan_card/domain/captured_image.dart';
import '../scan_card/domain/scan_state.dart';
import '../scan_card/presentation/scan_controller.dart';

class HomeScreen extends ConsumerStatefulWidget {
  const HomeScreen({super.key});

  @override
  ConsumerState<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends ConsumerState<HomeScreen> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) async {
      final recovered = await ref
          .read(scanControllerProvider.notifier)
          .recoverLostImage();
      if (recovered && mounted) {
        await context.push(AppRoutes.scan);
      }
    });
  }

  Future<void> _capture(CardImageSource source) async {
    final ready = await ref
        .read(scanControllerProvider.notifier)
        .capture(source);
    if (ready && mounted) {
      await context.push(AppRoutes.scan);
    }
  }

  @override
  Widget build(BuildContext context) {
    ref.listen(scanControllerProvider, (_, next) {
      if (next is ScanFailed) {
        ScaffoldMessenger.of(context)
          ..hideCurrentSnackBar()
          ..showSnackBar(SnackBar(content: Text(next.message)));
        ref.read(scanControllerProvider.notifier).acknowledgeError();
      }
    });

    final picking = ref.watch(scanControllerProvider) is ScanPicking;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Business Card Scanner'),
        actions: const [_AccountMenu()],
      ),
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Icon(Icons.badge_outlined, size: 96),
              const SizedBox(height: 16),
              Text(
                'Scan a business card and save it to your Outlook contacts.',
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.bodyLarge,
              ),
              const SizedBox(height: 32),
              FilledButton.icon(
                onPressed: picking
                    ? null
                    : () => _capture(CardImageSource.camera),
                icon: const Icon(Icons.photo_camera_outlined),
                label: const Text('Scan Business Card'),
              ),
              const SizedBox(height: 12),
              OutlinedButton.icon(
                onPressed: picking
                    ? null
                    : () => _capture(CardImageSource.gallery),
                icon: const Icon(Icons.photo_library_outlined),
                label: const Text('Choose from Gallery'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _AccountMenu extends ConsumerWidget {
  const _AccountMenu();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final auth = ref.watch(authControllerProvider);
    final account = auth is SignedIn ? auth.account : null;

    return PopupMenuButton<void>(
      icon: const Icon(Icons.account_circle_outlined),
      tooltip: 'Account',
      itemBuilder: (context) => [
        if (account != null)
          PopupMenuItem<void>(
            enabled: false,
            child: ListTile(
              contentPadding: EdgeInsets.zero,
              title: Text(account.displayName),
              subtitle: Text(account.username),
            ),
          ),
        PopupMenuItem<void>(
          onTap: () => ref.read(authControllerProvider.notifier).signOut(),
          child: const ListTile(
            contentPadding: EdgeInsets.zero,
            leading: Icon(Icons.logout),
            title: Text('Sign out'),
          ),
        ),
      ],
    );
  }
}
