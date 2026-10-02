import 'package:flutter/material.dart';

class ConfigErrorScreen extends StatelessWidget {
  const ConfigErrorScreen({required this.problems, super.key});

  final List<String> problems;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Configuration required')),
      body: ListView(
        padding: const EdgeInsets.all(24),
        children: [
          const Text(
            'This build is missing required settings. Run with '
            '--dart-define-from-file=dart_defines.json (see README).',
          ),
          const SizedBox(height: 16),
          for (final problem in problems)
            ListTile(
              leading: const Icon(Icons.error_outline),
              title: Text(problem),
            ),
        ],
      ),
    );
  }
}
