import 'package:flutter/material.dart';

import '../data/contacts_api.dart';
import 'save_contact_controller.dart';

sealed class DuplicateDecision {
  const DuplicateDecision();
}

final class UpdateExisting extends DuplicateDecision {
  const UpdateExisting(this.match);

  final DuplicateMatch match;
}

final class CreateAnyway extends DuplicateDecision {
  const CreateAnyway();
}

final class CancelSave extends DuplicateDecision {
  const CancelSave();
}

/// "Possible existing contact found." Lets the user update one of the matches,
/// create a new contact anyway, or go back. Dismissing counts as cancel.
Future<DuplicateDecision> showDuplicateDialog(
  BuildContext context,
  List<DuplicateMatch> matches,
) async {
  final decision = await showDialog<DuplicateDecision>(
    context: context,
    builder: (context) => DuplicateDialog(matches: matches),
  );
  return decision ?? const CancelSave();
}

class DuplicateDialog extends StatefulWidget {
  const DuplicateDialog({required this.matches, super.key});

  final List<DuplicateMatch> matches;

  @override
  State<DuplicateDialog> createState() => _DuplicateDialogState();
}

class _DuplicateDialogState extends State<DuplicateDialog> {
  late DuplicateMatch _selected = widget.matches.first;

  @override
  Widget build(BuildContext context) {
    final several = widget.matches.length > 1;
    return AlertDialog(
      title: const Text(SaveContactController.duplicateTitle),
      content: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              several
                  ? 'These Outlook contacts may be the same person. Choose one to update, or create a new contact.'
                  : 'This Outlook contact may be the same person.',
            ),
            const SizedBox(height: 12),
            RadioGroup<String>(
              groupValue: _selected.id,
              onChanged: (id) => setState(
                () => _selected = widget.matches.firstWhere((m) => m.id == id),
              ),
              child: Column(
                children: [
                  for (final match in widget.matches)
                    several
                        ? RadioListTile<String>(
                            contentPadding: EdgeInsets.zero,
                            value: match.id,
                            title: Text(match.displayName),
                            subtitle: _MatchDetails(match: match),
                          )
                        : ListTile(
                            contentPadding: EdgeInsets.zero,
                            leading: const Icon(Icons.person_outline),
                            title: Text(match.displayName),
                            subtitle: _MatchDetails(match: match),
                          ),
                ],
              ),
            ),
          ],
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(const CancelSave()),
          child: const Text('Cancel'),
        ),
        TextButton(
          onPressed: () => Navigator.of(context).pop(const CreateAnyway()),
          child: const Text('Create anyway'),
        ),
        FilledButton(
          onPressed: () => Navigator.of(context).pop(UpdateExisting(_selected)),
          child: const Text('Update existing'),
        ),
      ],
    );
  }
}

class _MatchDetails extends StatelessWidget {
  const _MatchDetails({required this.match});

  final DuplicateMatch match;

  @override
  Widget build(BuildContext context) {
    final details = [?match.companyName, ?match.email, ?match.mobilePhone];
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (details.isNotEmpty) Text(details.join(' · ')),
        Text(
          match.reasons.map((r) => r.label).join(' · '),
          style: TextStyle(color: Theme.of(context).colorScheme.tertiary),
        ),
      ],
    );
  }
}
