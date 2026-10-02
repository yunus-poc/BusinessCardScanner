import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/errors/api_failure.dart';
import '../../contact_preview/domain/contact_draft.dart';
import '../data/contacts_api.dart';

sealed class SaveState {
  const SaveState();
}

final class SaveIdle extends SaveState {
  const SaveIdle();
}

enum SaveStep { checkingDuplicates, saving }

final class Saving extends SaveState {
  const Saving(this.step);

  final SaveStep step;
}

/// The user must choose: update one of [matches], create anyway, or cancel.
final class DuplicateFound extends SaveState {
  const DuplicateFound(this.draft, this.matches);

  final ContactDraft draft;
  final List<DuplicateMatch> matches;
}

final class Saved extends SaveState {
  const Saved(this.contact, {this.updatedExisting = false});

  final SavedContact contact;
  final bool updatedExisting;
}

final class SaveFailed extends SaveState {
  const SaveFailed(this.failure);

  final ApiFailure failure;
}

/// Save flow: duplicate check → create, or → user decision → update / create.
class SaveContactController extends Notifier<SaveState> {
  static const successMessage = 'Contact saved to Outlook.';
  static const duplicateTitle = 'Possible existing contact found.';

  ContactsApi get _api => ref.read(contactsApiProvider);

  @override
  SaveState build() => const SaveIdle();

  /// One operation at a time: taps while busy or while a decision is pending
  /// are ignored, so a slow network cannot create the contact twice.
  Future<void> save(ContactDraft draft) async {
    if (state is Saving || state is DuplicateFound) {
      return;
    }
    state = const Saving(SaveStep.checkingDuplicates);
    final List<DuplicateMatch> matches;
    try {
      matches = await _api.checkDuplicates(draft);
    } on ApiFailure catch (failure) {
      _set(SaveFailed(failure));
      return;
    }
    if (!ref.mounted) {
      return;
    }
    if (matches.isNotEmpty) {
      state = DuplicateFound(draft, matches);
      return;
    }
    await _run(SaveStep.saving, () async => Saved(await _api.create(draft)));
  }

  Future<void> createAnyway() async {
    if (state case DuplicateFound(:final draft)) {
      await _run(SaveStep.saving, () async => Saved(await _api.create(draft)));
    }
  }

  Future<void> updateExisting(DuplicateMatch match) async {
    if (state case DuplicateFound(:final draft)) {
      await _run(
        SaveStep.saving,
        () async =>
            Saved(await _api.update(match.id, draft), updatedExisting: true),
      );
    }
  }

  /// Back to the form, nothing saved.
  void cancel() => state = const SaveIdle();

  void reset() => state = const SaveIdle();

  Future<void> _run(SaveStep step, Future<SaveState> Function() action) async {
    state = Saving(step);
    try {
      _set(await action());
    } on ApiFailure catch (failure) {
      _set(SaveFailed(failure));
    }
  }

  void _set(SaveState next) {
    if (ref.mounted) {
      state = next;
    }
  }
}

final saveContactControllerProvider =
    NotifierProvider<SaveContactController, SaveState>(
      SaveContactController.new,
    );
