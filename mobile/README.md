# Business Card Scanner (mobile)

Flutter app for iOS and Android. Setup, configuration (`dart_defines.json`), Entra sign-in, iOS/Android
specifics and release builds are documented in the repository [README](../README.md); the design is in
[ARCHITECTURE.md](../ARCHITECTURE.md).

```bash
flutter pub get
flutter analyze
flutter test
flutter run --dart-define-from-file=dart_defines.json
```

| Folder | Contents |
|---|---|
| `lib/app/` | App shell, routes, router guard, API client wiring |
| `lib/core/` | Config, HTTP client + auth interceptor, `ApiFailure`, theme |
| `lib/features/authentication/` | Entra sign-in (`AuthService`), secure token storage, login/splash |
| `lib/features/scan_card/` | Capture, on-device OCR (ML Kit), layout feature extraction |
| `lib/features/contact_preview/` | Parse API, editable draft, review UI, scan telemetry |
| `lib/features/contacts/` | Duplicate check, save/update to Outlook, result screen |
