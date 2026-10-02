import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../app/routes.dart';
import '../../../core/errors/api_failure.dart';
import '../../contacts/presentation/duplicate_dialog.dart';
import '../../contacts/presentation/save_contact_controller.dart';
import '../domain/contact_draft.dart';
import '../domain/parsed_card.dart';
import 'contact_preview_controller.dart';

/// Editable preview of the parsed card. Every field stays editable; fields the
/// parser is unsure about are highlighted until the user touches them.
class ContactPreviewScreen extends ConsumerWidget {
  const ContactPreviewScreen({super.key});

  static const fallbackBanner =
      'AI assistance was unavailable, so some fields were filled from the card '
      'layout. Please check the highlighted fields.';

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final state = ref.watch(contactPreviewControllerProvider);
    final saving = ref.watch(saveContactControllerProvider) is Saving;

    ref.listen(saveContactControllerProvider, (_, next) {
      switch (next) {
        case Saved():
          // The contact lives in Outlook now; nothing about the card stays in the app.
          ref.read(contactPreviewControllerProvider.notifier).reset();
          context.go(AppRoutes.saved);
        case DuplicateFound(:final matches):
          unawaited(
            _resolveDuplicate(
              showDuplicateDialog(context, matches),
              ref.read(saveContactControllerProvider.notifier),
            ),
          );
        case _:
          break;
      }
    });

    return PopScope(
      // No leaving mid-save: the user must learn whether the contact was created.
      canPop: !saving,
      // Leaving drops the OCR text and the draft from memory.
      onPopInvokedWithResult: (didPop, _) {
        if (didPop) {
          ref.read(contactPreviewControllerProvider.notifier).reset();
          ref.read(saveContactControllerProvider.notifier).reset();
        }
      },
      child: Scaffold(
        appBar: AppBar(title: const Text('Review contact')),
        body: SafeArea(
          child: switch (state) {
            PreviewReady(:final draft, :final ocrToPreview) => _ContactForm(
              draft: draft,
              ocrToPreview: ocrToPreview,
            ),
            PreviewFailed(:final failure) => _FailedView(failure: failure),
            PreviewIdle() || PreviewLoading() => const _PreparingView(),
          },
        ),
        bottomNavigationBar: state is PreviewReady
            ? _SaveBar(draft: state.draft)
            : null,
      ),
    );
  }
}

/// Applies the user's choice from the duplicate dialog. Takes the dialog future
/// rather than a BuildContext, so no context is used across the await.
Future<void> _resolveDuplicate(
  Future<DuplicateDecision> decision,
  SaveContactController save,
) async {
  switch (await decision) {
    case UpdateExisting(:final match):
      await save.updateExisting(match);
    case CreateAnyway():
      await save.createAnyway();
    case CancelSave():
      save.cancel();
  }
}

class _PreparingView extends StatelessWidget {
  const _PreparingView();

  @override
  Widget build(BuildContext context) {
    return const Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          CircularProgressIndicator(),
          SizedBox(height: 16),
          Text('Preparing contact…'),
        ],
      ),
    );
  }
}

class _FailedView extends ConsumerWidget {
  const _FailedView({required this.failure});

  final ApiFailure failure;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final controller = ref.read(contactPreviewControllerProvider.notifier);
    final theme = Theme.of(context);
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.cloud_off_outlined, size: 56),
            const SizedBox(height: 16),
            Text(failure.message, textAlign: TextAlign.center),
            const SizedBox(height: 24),
            if (failure.retryable) ...[
              FilledButton(
                onPressed: controller.retry,
                child: const Text('Try again'),
              ),
              const SizedBox(height: 8),
            ],
            OutlinedButton(
              onPressed: controller.enterManually,
              child: const Text('Enter manually'),
            ),
            if (failure.traceId case final traceId?) ...[
              const SizedBox(height: 16),
              SelectableText(
                'Reference: $traceId',
                style: theme.textTheme.bodySmall,
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class _ContactForm extends ConsumerWidget {
  const _ContactForm({required this.draft, this.ocrToPreview});

  final ContactDraft draft;
  final Duration? ocrToPreview;

  static const _sections = <(String, List<ContactField>)>[
    ('Person', [ContactField.firstName, ContactField.lastName]),
    (
      'Work',
      [
        ContactField.companyName,
        ContactField.jobTitle,
        ContactField.department,
      ],
    ),
    (
      'Contact',
      [
        ContactField.email,
        ContactField.mobilePhone,
        ContactField.businessPhone,
        ContactField.website,
      ],
    ),
    (
      'Address',
      [
        ContactField.street,
        ContactField.city,
        ContactField.state,
        ContactField.postalCode,
        ContactField.country,
      ],
    ),
    ('Notes', [ContactField.notes]),
  ];

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final controller = ref.read(contactPreviewControllerProvider.notifier);
    final card = draft.card;
    final errors = draft.errors;
    // New key per parse result, so a retry or manual entry re-initializes inputs.
    final generation = identityHashCode(card);

    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
      children: [
        if (draft.manualEntry)
          const _Banner(
            icon: Icons.edit_note,
            text: 'Enter the contact details.',
          )
        else if (card.fallbackUsed)
          const _Banner(
            icon: Icons.info_outline,
            text: ContactPreviewScreen.fallbackBanner,
          )
        else if (draft.reviewFields.isNotEmpty)
          _Banner(
            icon: Icons.fact_check_outlined,
            text: draft.reviewFields.length == 1
                ? '1 field needs a quick check.'
                : '${draft.reviewFields.length} fields need a quick check.',
          ),
        if (kDebugMode && ocrToPreview != null)
          Padding(
            padding: const EdgeInsets.only(bottom: 8),
            child: Text(
              'OCR → preview ${ocrToPreview!.inMilliseconds} ms · LLM ${card.llmStatus.name}',
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ),
        for (final (title, fields) in _sections) ...[
          _SectionTitle(title),
          for (final field in fields)
            _FieldInput(
              key: ValueKey('${field.name}-$generation'),
              field: field,
              initialValue: draft[field],
              needsReview: draft.reviewFields.contains(field),
              confidence: card[field].value == null
                  ? null
                  : card[field].confidence,
              error: errors[field],
              onChanged: (value) => controller.updateField(field, value),
            ),
        ],
        if (card.additionalEmails.isNotEmpty ||
            card.additionalPhones.isNotEmpty) ...[
          const _SectionTitle('Also found on the card'),
          for (final email in card.additionalEmails)
            CheckboxListTile(
              contentPadding: EdgeInsets.zero,
              value: draft.includedEmails.contains(email),
              title: Text(email),
              subtitle: const Text('Additional email'),
              onChanged: (v) =>
                  controller.includeEmail(email, included: v ?? false),
            ),
          for (final phone in card.additionalPhones)
            CheckboxListTile(
              contentPadding: EdgeInsets.zero,
              value: draft.includedPhones.contains(phone),
              title: Text(phone),
              subtitle: const Text('Additional phone'),
              onChanged: (v) =>
                  controller.includePhone(phone, included: v ?? false),
            ),
        ],
      ],
    );
  }
}

class _FieldInput extends StatefulWidget {
  const _FieldInput({
    required this.field,
    required this.initialValue,
    required this.needsReview,
    required this.confidence,
    required this.error,
    required this.onChanged,
    super.key,
  });

  final ContactField field;
  final String initialValue;
  final bool needsReview;
  final double? confidence;
  final String? error;
  final ValueChanged<String> onChanged;

  @override
  State<_FieldInput> createState() => _FieldInputState();
}

class _FieldInputState extends State<_FieldInput> {
  late final _controller = TextEditingController(text: widget.initialValue);

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    final field = widget.field;
    final reviewBorder = OutlineInputBorder(
      borderSide: BorderSide(color: colors.tertiary, width: 2),
    );
    final multiline =
        field == ContactField.notes || field == ContactField.street;

    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: TextField(
        controller: _controller,
        keyboardType: field.keyboardType,
        textCapitalization: switch (field) {
          ContactField.email || ContactField.website => TextCapitalization.none,
          ContactField.notes => TextCapitalization.sentences,
          _ => TextCapitalization.words,
        },
        autocorrect: false,
        minLines: 1,
        maxLines: multiline ? 4 : 1,
        onChanged: widget.onChanged,
        decoration: InputDecoration(
          labelText: field.label,
          errorText: widget.error,
          helperText: widget.needsReview ? 'Please check' : null,
          helperStyle: TextStyle(color: colors.tertiary),
          enabledBorder: widget.needsReview ? reviewBorder : null,
          prefixIcon: widget.needsReview
              ? Icon(
                  Icons.error_outline,
                  color: colors.tertiary,
                  semanticLabel: 'Needs review',
                )
              : null,
          suffixIcon: widget.confidence == null
              ? null
              : Tooltip(
                  message: 'Confidence ${(widget.confidence! * 100).round()}%',
                  triggerMode: TooltipTriggerMode.tap,
                  child: const Icon(Icons.info_outline, size: 18),
                ),
        ),
      ),
    );
  }
}

class _SaveBar extends ConsumerWidget {
  const _SaveBar({required this.draft});

  final ContactDraft draft;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final save = ref.watch(saveContactControllerProvider);
    // A request is running (spinner) vs. the duplicate dialog awaits a decision
    // (disabled, but nothing is in progress).
    final requestRunning = save is Saving;
    final busy = requestRunning || save is DuplicateFound;
    final failure = save is SaveFailed ? save.failure : null;
    final colors = Theme.of(context).colorScheme;

    final hint = !draft.hasIdentity
        ? 'Add a name, company, email or phone to save.'
        : draft.errors.isNotEmpty
        ? 'Fix the highlighted errors to save.'
        : null;

    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (failure != null)
              Padding(
                padding: const EdgeInsets.only(bottom: 8),
                child: Text(
                  failure.message,
                  textAlign: TextAlign.center,
                  style: TextStyle(color: colors.error),
                ),
              )
            else if (hint != null)
              Padding(
                padding: const EdgeInsets.only(bottom: 8),
                child: Text(hint, textAlign: TextAlign.center),
              ),
            FilledButton.icon(
              onPressed: draft.canSave && !busy
                  ? () => ref
                        .read(saveContactControllerProvider.notifier)
                        .save(draft)
                  : null,
              icon: requestRunning
                  ? const SizedBox.square(
                      dimension: 18,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Icon(Icons.save_outlined),
              label: Text(
                save is Saving && save.step == SaveStep.checkingDuplicates
                    ? 'Checking for duplicates…'
                    : requestRunning
                    ? 'Saving…'
                    // A non-retryable failure needs a change first; the button stays "Save".
                    : failure != null && failure.retryable
                    ? 'Try again'
                    : 'Save to Outlook',
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _Banner extends StatelessWidget {
  const _Banner({required this.icon, required this.text});

  final IconData icon;
  final String text;

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    return Container(
      margin: const EdgeInsets.only(bottom: 12),
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: colors.tertiaryContainer,
        borderRadius: BorderRadius.circular(8),
      ),
      child: Row(
        children: [
          Icon(icon, color: colors.onTertiaryContainer),
          const SizedBox(width: 12),
          Expanded(
            child: Text(
              text,
              style: TextStyle(color: colors.onTertiaryContainer),
            ),
          ),
        ],
      ),
    );
  }
}

class _SectionTitle extends StatelessWidget {
  const _SectionTitle(this.title);

  final String title;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(top: 12, bottom: 8),
      child: Text(title, style: Theme.of(context).textTheme.titleSmall),
    );
  }
}
