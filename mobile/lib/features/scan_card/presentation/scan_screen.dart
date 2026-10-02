import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../app/routes.dart';
import '../../contact_preview/presentation/contact_preview_controller.dart';
import '../domain/captured_image.dart';
import '../domain/scan_state.dart';
import 'scan_controller.dart';

/// Capture result → on-device OCR → hand-off to the contact preview.
class ScanScreen extends ConsumerStatefulWidget {
  const ScanScreen({super.key});

  @override
  ConsumerState<ScanScreen> createState() => _ScanScreenState();
}

class _ScanScreenState extends ConsumerState<ScanScreen> {
  var _handedOff = false;

  @override
  void initState() {
    super.initState();
    // fireImmediately: OCR may already be done before this screen's first frame.
    ref.listenManual(
      scanControllerProvider,
      _onScanState,
      fireImmediately: true,
    );
  }

  /// As soon as OCR is done, the layout lines go to the preview and this screen
  /// is replaced, so the user goes straight from "Reading card…" to the contact.
  void _onScanState(ScanState? previous, ScanState next) {
    if (next is! ScanRecognized || _handedOff) {
      return;
    }
    _handedOff = true;
    // Deferred: providers must not change while widgets are building.
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) {
        return;
      }
      ref
          .read(contactPreviewControllerProvider.notifier)
          .start(next.layout, ocrDuration: next.result.duration);
      ref.read(scanControllerProvider.notifier).reset();
      context.pushReplacement(AppRoutes.preview);
    });
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(scanControllerProvider);
    final source = switch (state) {
      ScanRecognizing(:final image) => image.source,
      ScanRecognized(:final source) || ScanOcrFailed(:final source) => source,
      ScanPicking(:final source) => source,
      _ => null,
    };

    return PopScope(
      // Any way out of this screen deletes the temporary image.
      onPopInvokedWithResult: (didPop, _) {
        if (didPop) {
          ref.read(scanControllerProvider.notifier).reset();
        }
      },
      child: Scaffold(
        appBar: AppBar(title: const Text('Business card')),
        body: SafeArea(
          child: switch (state) {
            ScanRecognizing(:final image) => _RecognizingView(image: image),

            ScanOcrFailed() => _FailedView(state: state),
            _ => const Center(child: CircularProgressIndicator()),
          },
        ),
        bottomNavigationBar: source == null || state is ScanPicking
            ? null
            : SafeArea(
                child: Padding(
                  padding: const EdgeInsets.all(16),
                  child: OutlinedButton.icon(
                    onPressed: state is ScanRecognizing
                        ? null
                        : () => _retake(source),
                    icon: const Icon(Icons.refresh),
                    label: Text(
                      source == CardImageSource.camera
                          ? 'Retake photo'
                          : 'Choose another photo',
                    ),
                  ),
                ),
              ),
      ),
    );
  }

  Future<void> _retake(CardImageSource source) async {
    final started = await ref
        .read(scanControllerProvider.notifier)
        .capture(source);
    if (!started && mounted) {
      // Cancelled or failed: go back to home, which shows any error.
      context.pop();
    }
  }
}

class _RecognizingView extends StatelessWidget {
  const _RecognizingView({required this.image});

  final CapturedImage image;

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        const LinearProgressIndicator(),
        Expanded(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: ClipRRect(
              borderRadius: BorderRadius.circular(12),
              child: Image.file(
                File(image.path),
                fit: BoxFit.contain,
                // Decode at screen size, not at the full 2048 px.
                cacheWidth: 1024,
                errorBuilder: (context, error, stackTrace) =>
                    const Center(child: Icon(Icons.image_outlined, size: 48)),
              ),
            ),
          ),
        ),
        const Padding(
          padding: EdgeInsets.only(bottom: 8),
          child: Text('Reading card…'),
        ),
      ],
    );
  }
}

class _FailedView extends ConsumerWidget {
  const _FailedView({required this.state});

  final ScanOcrFailed state;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.text_snippet_outlined, size: 56),
            const SizedBox(height: 16),
            Text(state.message, textAlign: TextAlign.center),
            if (state.canRetry) ...[
              const SizedBox(height: 16),
              FilledButton(
                onPressed: () => ref
                    .read(scanControllerProvider.notifier)
                    .retryRecognition(),
                child: const Text('Try again'),
              ),
            ],
          ],
        ),
      ),
    );
  }
}
