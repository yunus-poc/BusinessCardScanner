import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/errors/api_failure.dart';
import '../../scan_card/domain/layout_line.dart';
import '../data/business_card_api.dart';
import '../data/scan_telemetry_api.dart';
import '../domain/contact_draft.dart';
import '../domain/parsed_card.dart';

sealed class PreviewState {
  const PreviewState();
}

final class PreviewIdle extends PreviewState {
  const PreviewIdle();
}

final class PreviewLoading extends PreviewState {
  const PreviewLoading();
}

final class PreviewReady extends PreviewState {
  const PreviewReady(this.draft, {this.ocrToPreview});

  final ContactDraft draft;

  /// OCR finished → editable preview shown. Measured, not assumed (debug display;
  /// reported via ScanTelemetryApi). Null for manual entry.
  final Duration? ocrToPreview;
}

final class PreviewFailed extends PreviewState {
  const PreviewFailed(this.failure);

  final ApiFailure failure;
}

/// Sends the recognized layout to the API and holds the editable draft.
/// The OCR lines stay in memory only for a retry and are dropped on [reset].
class ContactPreviewController extends Notifier<PreviewState> {
  BusinessCardApi get _api => ref.read(businessCardApiProvider);
  ScanTelemetryApi get _telemetry => ref.read(scanTelemetryApiProvider);

  List<LayoutLine> _layout = const [];
  Stopwatch? _sinceOcr;
  Duration? _ocrDuration;
  CancelToken? _inFlight;

  @override
  PreviewState build() {
    ref.onDispose(() => _inFlight?.cancel());
    return const PreviewIdle();
  }

  /// Called the moment on-device OCR has finished.
  Future<void> start(List<LayoutLine> layout, {Duration? ocrDuration}) {
    _layout = layout;
    _ocrDuration = ocrDuration;
    _sinceOcr = Stopwatch()..start();
    return _load();
  }

  Future<void> retry() => _load();

  /// Lets the user type the contact when parsing failed; no rescan needed.
  void enterManually() {
    _inFlight?.cancel();
    state = PreviewReady(
      ContactDraft.fromCard(ParsedCard.empty, manualEntry: true),
    );
  }

  void updateField(ContactField field, String value) =>
      _updateDraft((d) => d.edit(field, value));

  void includeEmail(String email, {required bool included}) =>
      _updateDraft((d) => d.includeEmail(email, included: included));

  void includePhone(String phone, {required bool included}) =>
      _updateDraft((d) => d.includePhone(phone, included: included));

  /// Leaves the flow: cancels any request and forgets OCR text and draft.
  void reset() {
    _inFlight?.cancel();
    _inFlight = null;
    _layout = const [];
    _sinceOcr = null;
    state = const PreviewIdle();
  }

  Future<void> _load() async {
    _inFlight?.cancel();
    final token = _inFlight = CancelToken();
    state = const PreviewLoading();
    try {
      final card = await _api.parse(_layout, cancelToken: token);
      if (!_isCurrent(token)) {
        return;
      }
      final ocrToPreview = _sinceOcr?.elapsed;
      _sinceOcr?.stop();
      state = PreviewReady(
        ContactDraft.fromCard(card),
        ocrToPreview: ocrToPreview,
      );
      _reportTiming(ocrToPreview, card.llmStatus);
    } on ApiFailure catch (failure) {
      if (_isCurrent(token) && failure.code != ApiFailure.cancelled) {
        state = PreviewFailed(failure);
      }
    }
  }

  /// Only the first successful preview of a scan is reported (not retries after a
  /// failure, whose timing would include the user's wait).
  void _reportTiming(Duration? ocrToPreview, LlmStatus llmStatus) {
    final ocr = _ocrDuration;
    _ocrDuration = null;
    if (ocr != null && ocrToPreview != null) {
      // Fire-and-forget, and any failure is swallowed here, so telemetry can
      // never surface as an unhandled error, whatever the API layer throws.
      unawaited(
        _telemetry
            .reportScan(
              ocr: ocr,
              ocrToPreview: ocrToPreview,
              llmStatus: llmStatus,
            )
            .catchError((Object _) {}),
      );
    }
  }

  bool _isCurrent(CancelToken token) =>
      ref.mounted && identical(_inFlight, token) && !token.isCancelled;

  void _updateDraft(ContactDraft Function(ContactDraft) change) {
    if (state case PreviewReady(:final draft, :final ocrToPreview)) {
      state = PreviewReady(change(draft), ocrToPreview: ocrToPreview);
    }
  }
}

final contactPreviewControllerProvider =
    NotifierProvider<ContactPreviewController, PreviewState>(
      ContactPreviewController.new,
    );
