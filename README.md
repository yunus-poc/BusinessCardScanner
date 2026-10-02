# Business Card Scanner

Scan a physical business card on an iOS or Android phone, review the extracted contact, and save it to
your Microsoft 365 / Outlook contacts.

- **OCR runs on the device** (Google ML Kit). The card image never leaves the phone.
- A **.NET 8 API** turns OCR lines and layout hints into a structured contact. It uses deterministic
  rules for email, phone and URL, plus a **self-hosted Phi-4-mini-instruct** model for names, company,
  title and address.
- If the model is slow or down, you still get an editable result from the deterministic parser.
- Contacts are saved through **Microsoft Graph** with the signed-in user's own delegated permissions.

See [ARCHITECTURE.md](ARCHITECTURE.md) for the design and the reasoning behind it.

> **Status:** MVP complete (production-readiness pass done). The model benchmark was deliberately skipped; see
> [ARCHITECTURE.md → Final review](ARCHITECTURE.md#11-final-architecture-review) for open risks and manual steps.

---

## Repository layout

```
backend/   .NET 8 solution (Api, Application, Domain, Infrastructure + tests)
mobile/    Flutter app (Android + iOS)
inference/ Self-hosted LLM service (llama.cpp + Phi-4-mini), prompt/schema contract, scripts
```

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 8.0+ (9.x works) | `global.json` rolls forward to the newest installed SDK; projects target `net8.0` |
| .NET runtime | 8.0 | Needed to run the API and tests |
| Flutter | 3.47+ stable (Dart 3.13+) | Matches the `sdk` constraint in `mobile/pubspec.yaml` |
| Android Studio + Android SDK | latest | For Android builds and the emulator |
| Xcode | 16+ (macOS only) | For iOS builds; ML Kit requires a physical device or arm64 simulator |
| Docker | 24+ with Compose v2.20+ | API container and LLM service (`include:` needs Compose 2.20) |
| Microsoft Entra ID tenant | n/a | Permission to create App Registrations, or an admin who can |

## Local development

### Backend

```bash
cd backend
dotnet build
dotnet test
dotnet run --project src/BusinessCard.Api
```

The API listens on `http://localhost:5115` (and `https://localhost:7292` with the `https` profile).
Swagger UI is at `/swagger` in Development.

### Mobile

```bash
cd mobile
flutter pub get
flutter analyze
flutter test
flutter run --dart-define-from-file=dart_defines.json
```

Copy `dart_defines.example.json` to `dart_defines.json` (git-ignored) and fill it in. Without the Entra
values the app starts on a *Configuration required* screen that lists what is missing.

| Key | Value |
|---|---|
| `API_BASE_URL` | Android emulator `http://10.0.2.2:5115` · iOS simulator `http://localhost:5115` · physical device: an **HTTPS** dev tunnel (e.g. VS Dev Tunnels). Release builds refuse anything but `https://` |
| `ENTRA_TENANT_ID` | Directory (tenant) ID |
| `ENTRA_CLIENT_ID` | Client ID of the **mobile** registration (not the API's) |
| `ENTRA_API_SCOPE` | `api://<api-client-id>/access_as_user` |
| `ENTRA_REDIRECT_URI` | `com.example.businesscardscanner://oauthredirect` (default) |

None of these are secrets; they identify public endpoints and are visible in any app binary.

## Configuration

Backend settings follow the standard ASP.NET Core order: `appsettings.json` → `appsettings.{Environment}.json`
→ user secrets (Development) → environment variables. **No secrets are committed.** Use
`dotnet user-secrets` locally and environment variables or a secret store in deployed environments.
Environment variables use `__` for nesting, e.g. `Serilog__MinimumLevel__Default=Debug`.

For readable local logs, copy `backend/src/BusinessCard.Api/appsettings.Development.json.example` to
`appsettings.Development.json` (git-ignored). The default output is compact JSON, one event per line.

| Setting | Default | Purpose |
|---|---|---|
| `Kestrel:Limits:MaxRequestBodySize` | `262144` (256 KB) | Parse payloads are a few KB; anything larger is rejected with 413 |
| `RateLimiting:PermitsPerMinute` | `60` | Per signed-in user (object id); `/health*` exempt |
| `ForwardedHeaders:KnownNetworks` | `[]` | CIDRs of the TLS proxy allowed to set `X-Forwarded-For/Proto` (one hop). Empty = only loopback is trusted |
| `Telemetry:OtlpEndpoint` | *(unset)* | OTLP collector for metrics export; unset = no export |
| `Parsing:ReviewThreshold` | `0.80` | Fields scoring below are highlighted for review (0.5–0.99; tune on real cards) |
| `Ai:Provider` | `OpenAiCompatible` | `Disabled` runs without a model (every parse uses the deterministic fallback) |
| `Ai:BaseUrl` | `http://127.0.0.1:8080` | Inference service base URL (private network only) |
| `Ai:Model` | `phi-4-mini-instruct` | Sent in requests, logged as `LlmModel`; must match llama-server's `--alias` |
| `Ai:ApiKey` | *(none)* | **Secret.** Same value as `inference/secrets/llm_api_key.txt`; user secrets locally, `Ai__ApiKey` env / secret store elsewhere |
| `Ai:TimeoutMilliseconds` | `2500` | Whole-request budget; on expiry the parse falls back, with no retry |
| `Ai:MaxOutputTokens` | `128` | Output cap (the answer needs ~40–60 tokens) |
| `Serilog:MinimumLevel:*` | `Information`, framework `Warning` | `System.Net.Http.HttpClient` stays at `Warning` because it logs full URLs, which can contain PII from Graph `$filter` queries |

## Endpoints and error contract

| Endpoint | Auth | Purpose |
|---|---|---|
| `GET /health` | anonymous | Liveness: process is serving requests |
| `GET /health/ready` | anonymous | Readiness: LLM reachability, reported as `Degraded` (HTTP 200) rather than failing, because parsing still works without it |
| `GET /swagger` | Development only | OpenAPI UI (use **Authorize** to paste a bearer token) |
| `POST /api/business-cards/parse` | Bearer | OCR lines + layout hints → editable contact (nothing stored) |
| `POST /api/telemetry/scan` | Bearer | On-device timings `{ ocrMs, ocrToPreviewMs, llmStatus }` → `204` (numbers only) |
| everything else | Bearer token with `access_as_user` | See [Entra ID / App Registration](#entra-id--app-registration) |

Every error is `application/problem+json`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.30",
  "title": "Too Many Requests",
  "status": 429,
  "detail": "Outlook is busy. Please try again shortly.",
  "code": "GRAPH_RATE_LIMITED",
  "retryable": true,
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736"
}
```

- `code` is stable and machine-readable (`BusinessCard.Application/Errors/ErrorCodes.cs`):

  | Code | Status |
  |---|---|
  | `VALIDATION_FAILED` | 400 |
  | `UNAUTHENTICATED` / `FORBIDDEN` | 401 / 403 |
  | `NOT_FOUND` / `CONTACT_NOT_FOUND` | 404 |
  | `PAYLOAD_TOO_LARGE` / `UNSUPPORTED_MEDIA_TYPE` | 413 / 415 |
  | `RATE_LIMITED` (this API) / `GRAPH_RATE_LIMITED` (Outlook) | 429 |
  | `GRAPH_AUTHORIZATION_FAILED` | 403 |
  | `GRAPH_CONTACT_CREATE_FAILED` / `GRAPH_UNAVAILABLE` | 502 / 503 |
  | `INTERNAL_ERROR` | 500 |

  There are no OCR or LLM error codes: OCR runs on the device, and model problems never fail a parse (they
  show up as `meta.llmStatus` with `meta.fallbackUsed = true`). A possible duplicate is a normal `200` result.
- `retryable` is `true` for 408/429/502/503/504. The app offers **Retry** only then. 429 responses
  also carry `Retry-After`.
- `traceId` matches the `X-Trace-Id` response header (sent on every response) and the `@tr` / `TraceId`
  field in the logs. Ask users for it when they report a problem.
- Validation errors add `errors`, keyed by JSON path (`lines[3].text`).
- Unexpected failures return a generic 500; messages and stack traces are never sent to the client.

## Card parsing (backend)

`POST /api/business-cards/parse` accepts the layout lines from the app (max 80 lines, 300 chars each,
8,000 total; `region` and `size` required, `align`/`sameBlock`/`gap` optional) and returns every contact field
as `{ value, confidence, needsReview }` plus `additionalEmails`, `additionalPhones` and `meta`.

Deterministic stage (no AI), in `BusinessCard.Application/BusinessCards/Parsing/`:

| Step | What it does |
|---|---|
| Sanitize | NFC normalization; strips control, format, bidi-override and zero-width characters; collapses whitespace |
| Noise filter | Drops symbol-only lines, single characters, repeated-character stripes (logo/border artefacts). Conservative: `A.Ş.` and phone lines survive |
| Email | Repairs OCR damage (`ahmet @ acme , com`, Turkish letters) and flags repaired values for review; lowercased |
| Website | `https://`, `www.` or bare domains with a known TLD; normalized to `https://host/path`; email domains and social profiles are never the website (social links go to Notes) |
| Phone | **libphonenumber** with Türkiye as the default region; output in international format (`+90 532 123 45 67`). The number's type decides mobile/business; labels (`Tel`, `GSM`, `Cep`, `Fax`, `Faks`…) decide fax and ambiguous plans. **Fax numbers are dropped** (no fax field in Outlook contacts) |
| Postal code | Turkish 5-digit code with plate prefix 01–81 in an address-like line; city hint from the province named in the line (corroborated) or from the prefix (flagged for review) |

Lines consisting only of extracted values and labels are marked *consumed* and are not sent to the LLM.
Logs contain counts and timings only, never card text.

LLM stage (`BusinessCard.Infrastructure/Llm/PhiBusinessCardLlmClassifier.cs`): the remaining lines go to the
inference service as `<id> [REGION][SIZE] text` with the embedded contract prompt and JSON schema
(`inference/contract/`), temperature 0, one attempt, `Ai:TimeoutMilliseconds` budget. The answer is
**grounded** before use: line ids that were not sent, lines claimed twice, and city/state/country not written
on the card are discarded. Any model problem returns a normal 200 with deterministic fields and
`meta.fallbackUsed = true`; `meta.llmStatus` says why (`ok`, `timeout`, `unavailable`, `invalidResponse`,
`disabled`, `skipped`). `/health/ready` reports an unreachable model as `Degraded` (still HTTP 200).

```bash
cd backend/src/BusinessCard.Api
dotnet user-secrets set "Ai:ApiKey" "<contents of inference/secrets/llm_api_key.txt>"
```

Merge stage (`BusinessCard.Application/BusinessCards/Merging/`):

| Concern | Behaviour |
|---|---|
| Precedence | Email, phones, website, postal code: deterministic only. Semantic fields: grounded LLM answer; **layout heuristics** if the LLM failed (all such fields `needsReview`) |
| Residual text | Lines are sent to the LLM and mapped with extracted values and labels removed, so `Ahmet Yılmaz \| M: 0532…` yields the name `Ahmet Yılmaz` |
| Casing | Only ALL-CAPS/all-lowercase text is recased. Person names follow the card's language (`YILMAZ` → `Yılmaz`); company, title and place use Turkish rules only if that text has Turkish letters (`ENTERPRISE` → `Enterprise`, `YATIRIMLARI` in `ENERJİ YATIRIMLARI` → `Yatırımları`). Short caps words stay (`ACME`, `CEO`), legal forms normalized (`A.Ş.`, `Ltd. Şti.`, `GmbH`) |
| Names | Honorifics and post-nominals dropped (Dr., Prof., Av., PhD, MBA); last word = surname; particles (`van`, `de`, `bin`) join the surname |
| Address | Street = address lines minus postal code and city (district kept); provinces canonical (`ISTANBUL` → `İstanbul`); Türkiye inferred from a Turkish province (flagged unless the postal code corroborates) |
| Confidence | Heuristic, not a probability. Source base (LLM 0.80, layout 0.55) adjusted by layout prominence, email ↔ name, domain ↔ company, LLM ↔ layout agreement, city ↔ postal-code agreement, one-word names, grounding corrections. `needsReview` = below `Parsing:ReviewThreshold` (default 0.80) or produced by the fallback |

> The backend embeds `inference/contract/*` at build time, so the Docker build context must be the repository
> root (see [Deployment](#deployment-docker)).

## Observability

**Logs** (Serilog, compact JSON): one line per request (route template, status, duration, trace id), one per
parse (line counts, `LlmStatus`, `LlmModel`, `LlmDurationMs`, `ParserFallbackUsed`, review count), one per Graph
call (operation, status, attempts, `GraphDurationMs`, Graph `client-request-id`). **No PII**: no card text, names,
emails, phones, addresses, prompts, model answers, Graph error messages or tokens. `PiiLoggingTests` runs the whole
backend with only HTTP stubbed, the production log configuration, and app logging at Debug, and fails on any leak
(verified: enabling `System.Net.Http.HttpClient` at Information makes it fail on the Graph search URL).

**Metrics** (meter `BusinessCard`; tags are statuses/operations only):

| Instrument | Tags | Use |
|---|---|---|
| `businesscard.parse.duration` (ms) | `llm_status` | Backend parse p50 / p95 |
| `businesscard.llm.duration` (ms), `businesscard.llm.requests` | `model`, `status` | LLM p50 / p95, timeout rate, fallback rate |
| `businesscard.graph.duration` (ms), `businesscard.graph.requests` | `operation`, `outcome` | Graph failure rate (outcome = `success` or error code) |
| `businesscard.duplicates.checks` | `found` | Duplicate detection rate |
| `businesscard.client.ocr.duration`, `businesscard.client.ocr_to_preview.duration` (ms) | `llm_status` | On-device OCR and OCR → preview, reported by the app |

Plus ASP.NET Core's built-in `http.server.request.duration` (route-tagged) and rate-limiter metrics.

- Live, no setup: `dotnet-counters monitor -n BusinessCard.Api --counters BusinessCard,Microsoft.AspNetCore.Hosting`.
- Production: set `Telemetry:OtlpEndpoint` (e.g. `http://otel-collector:4317`) and point the collector at
  Prometheus/Grafana or Azure Monitor. **Only metrics are exported**, deliberately no traces: HTTP client spans
  record request URLs, and the Graph duplicate search carries an email address in its query string.
- The app reports on-device timings once per successful preview (fire-and-forget; failures are ignored).

> **Manual:** decide where metrics go (collector / Azure Monitor) and set `Telemetry__OtlpEndpoint`. Suggested
> alerts: LLM timeout rate > 20 % over 15 min; Graph failure rate > 5 %; parse p95 > 3 s.

## Entra ID / App Registration

> **Manual step.** Nothing here is automated. You need the *Application Developer* role (or higher) to create
> registrations, and a *Privileged Role Administrator* / *Cloud Application Administrator* to grant admin
> consent. Do this once per environment (dev, test, prod) or use one registration with separate redirect URIs;
> separate registrations per environment are recommended for production.

You create **two** registrations: one for the API (a confidential client that validates tokens and later calls
Graph On-Behalf-Of the user) and one for the mobile app (a public client that signs the user in).

```
Mobile app ──(token for api://<api-id>/access_as_user)──▶ BusinessCard.Api ──(OBO, delegated)──▶ Microsoft Graph
```

### 1. API registration: "BusinessCard API"

1. **Entra admin center → Identity → Applications → App registrations → New registration**
   - Name: `BusinessCard API (<env>)`
   - Supported account types: **Accounts in this organizational directory only** (single tenant)
   - Redirect URI: leave empty
2. Note the **Application (client) ID** and **Directory (tenant) ID** from *Overview*.
3. **Expose an API**
   - *Application ID URI* → **Add** → accept `api://<api-client-id>` → Save.
   - **Add a scope**:
     | Field | Value |
     |---|---|
     | Scope name | `access_as_user` |
     | Who can consent | Admins and users (admin consent is granted below anyway) |
     | Admin consent display name | `Access Business Card API as the signed-in user` |
     | Admin consent description | `Allows the Business Card Scanner app to parse business cards and save contacts on behalf of the signed-in user.` |
     | State | Enabled |
4. **Manifest** → set `"requestedAccessTokenVersion": 2` (under `api`) → Save.
   The API accepts both v1 and v2 tokens, but v2 is the modern format and makes `aud` the plain client ID.
5. **API permissions** → *Add a permission* → **Microsoft Graph** → **Delegated permissions**:
   - `User.Read`
   - `Contacts.ReadWrite`

   Then **Grant admin consent for <tenant>**. Do *not* add application permissions: the API acts only as the
   signed-in user, and contacts land in that user's own mailbox.
6. **Certificates & secrets**: needed for On-Behalf-Of (saving contacts).
   - Production: upload a **certificate** (preferred) and load it from Key Vault or the host's cert store.
   - Development: a **client secret** is acceptable; set a short expiry and store it only in user secrets.
   - Never put the secret in `appsettings*.json` or source control.

### 2. Mobile registration: "BusinessCard Mobile"

1. **New registration**
   - Name: `BusinessCard Mobile (<env>)`
   - Supported account types: **single tenant**
2. **Authentication** → *Add a platform* → **Mobile and desktop applications** → custom redirect URI
   `com.example.businesscardscanner://oauthredirect`. This one URI serves both iOS and Android. If you change
   the app ID (see *Changing the app identifier*), change this URI with it.
   - *Allow public client flows*: **No**. The app uses authorization code + PKCE, not ROPC or device code.
3. **API permissions** → *Add a permission* → **My APIs** → `BusinessCard API` → **Delegated** → `access_as_user`.
   Keep the default `User.Read`. Then **Grant admin consent**.
4. Optional but recommended: in the **API** registration's manifest, add the mobile client ID to
   `knownClientApplications` so any user consent covers both apps in one prompt.

### 3. Configure the API

`TenantId` and `ClientId` are identifiers, not secrets, but they differ per environment, so keep them out of
committed files.

```bash
cd backend/src/BusinessCard.Api
dotnet user-secrets set "AzureAd:TenantId" "<directory-tenant-id>"
dotnet user-secrets set "AzureAd:ClientId" "<api-client-id>"
```

Deployed environments use environment variables (`AzureAd__TenantId`, `AzureAd__ClientId`, …).

| Key | Required | Default | Notes |
|---|---|---|---|
| `AzureAd:Instance` | yes | `https://login.microsoftonline.com/` | Change only for sovereign clouds |
| `AzureAd:TenantId` | yes | | Directory (tenant) ID. Tokens from any other tenant are rejected |
| `AzureAd:ClientId` | yes | | API registration's client ID |
| `AzureAd:Scopes` | yes | `access_as_user` | Required delegated scope (space-separated if several) |
| `AzureAd:Audience` | no | *(unset)* | Leave unset: both `<client-id>` (v2) and `api://<client-id>` (v1) are accepted. Set only for a custom App ID URI |
| `AzureAd:ClientCredentials` | for saving | | Certificate or secret for OBO; secret store / env only. Without it the API starts, but saves fail with `GRAPH_AUTHORIZATION_FAILED` |

The API **refuses to start** if a required key is missing and names the missing key in the error.

### 4. What the API enforces

- Signature (Entra signing keys, auto-rotated from tenant metadata), **issuer** (your tenant only),
  **audience** (the API's client ID), **expiry** (1-minute clock skew).
- Scope `access_as_user` on **every** endpoint by default. Only `/health` and `/health/ready` are anonymous.
  A new endpoint is protected even if nobody annotates it.
- Missing or invalid token → `401` with `code: UNAUTHENTICATED` and a `WWW-Authenticate: Bearer` header.
  Valid token without the scope → `403` with `code: FORBIDDEN`.
- Unknown routes also return `401` to anonymous callers, so the API surface is not discoverable without a token.

### 5. Calling the API locally (Swagger / curl)

1. In the **API** registration → *Expose an API* → **Add a client application** → client ID
   `04b07795-8ddb-461a-bbee-02f9e1bf7b46` (Azure CLI), tick `access_as_user`. **Dev registration only**.
2. Get a token and paste it into Swagger's **Authorize** dialog:

```bash
az login --tenant <directory-tenant-id>
az account get-access-token --scope api://<api-client-id>/access_as_user --query accessToken -o tsv
```

### 6. Check with your identity team before rollout

- **Conditional Access**: if policies require a compliant/managed device or app protection (Intune MAM),
  sign-in must go through a broker (Microsoft Authenticator / Company Portal), which means an MSAL-based Flutter
  plugin rather than `flutter_appauth`. Ask which policies apply to this app.
- **Token lifetime / sign-in frequency** policies affect how often users re-authenticate.
- **User consent settings**: if user consent is disabled tenant-wide, admin consent (steps 1.5 and 2.3) is mandatory.

## Microsoft Graph permissions

Delegated `User.Read` and `Contacts.ReadWrite` only, on the **API** registration, called through On-Behalf-Of.
The mobile app never receives a Graph token.

### API credential for On-Behalf-Of

> **Manual.** Saving fails with `GRAPH_AUTHORIZATION_FAILED` ("Outlook access is not configured correctly")
> until the API has a credential (App Registration step 1.6).

Development (client secret, user secrets only):

```bash
cd backend/src/BusinessCard.Api
dotnet user-secrets set "AzureAd:ClientCredentials:0:SourceType" "ClientSecret"
dotnet user-secrets set "AzureAd:ClientCredentials:0:ClientSecret" "<secret value>"
```

Production (certificate in Azure Key Vault; the API's managed identity needs *get* on the certificate's secret):

```bash
AzureAd__ClientCredentials__0__SourceType=KeyVault
AzureAd__ClientCredentials__0__KeyVaultUrl=https://<vault>.vault.azure.net
AzureAd__ClientCredentials__0__KeyVaultCertificateName=<certificate-name>
```

Rotate before expiry; an expired credential shows up as `Graph token acquisition (On-Behalf-Of) failed: invalid_client`
in the logs.

### Saving contacts

| Endpoint | Purpose |
|---|---|
| `POST /api/contacts` | Create in the signed-in user's Outlook contacts → `201 { id, displayName }` |
| `POST /api/contacts/check-duplicate` | Possible existing contacts → `200 { possibleDuplicate, matches: [{ id, displayName, companyName, email, mobilePhone, reasons }] }` |
| `PUT /api/contacts/{id}` | Update an existing contact (after a duplicate warning): the card's non-empty fields overwrite, everything else is kept; emails and business phones are **merged** (max 3 emails) |

**Duplicate detection** (lightweight, not a dedup engine). Three Graph queries run in parallel: any of the card's
emails (`emailAddresses/any(...)`), the mobile number in the formats contacts are typically stored in
(international, E.164, national; Graph compares strings exactly), and first + last name. `DuplicateMatcher`
then decides:

| Reason | Rule |
|---|---|
| `email` | Any email equal (case-insensitive) |
| `mobilePhone` | The card's mobile equals the contact's mobile or a business phone, compared as E.164 |
| `nameAndCompany` | Same first and last name (case/diacritic-insensitive) **and** a matching company (`ACME` ~ `ACME Technology A.Ş.`). A name alone is never a duplicate |

Strongest reason first, at most 5 matches. A possible duplicate is **data, not an error** (always `200`). Filter
values are OData-quoted (`'` → `''`); a filter Graph rejects is skipped, but throttling or an outage fails the
check (the save would fail too). The app then shows **"Possible existing contact found."** with *Update existing*
(choosing the contact when there are several), *Create anyway* and *Cancel*.

- Graph REST v1.0 is called directly (`BusinessCard.Infrastructure/Graph/`): `givenName`, `surname`, `displayName`,
  `fileAs` (`Surname, Given`), `companyName`, `jobTitle`, `department`, `emailAddresses` (≤ 3), `mobilePhone`,
  `businessPhones`, `businessHomePage`, `businessAddress`, `personalNotes`.
- The server validates again (email, phone, http(s) website without credentials, lengths, control characters, at
  least one identifying field), trims, lowercases emails, normalizes phones to international format and
  de-duplicates. Display name falls back to company → email → phone.
- **Retries:** at most one inline retry, and only when Graph says it did not execute the request (429, or 503 with
  `Retry-After`) and the wait is ≤ `MicrosoftGraph:MaxInlineRetryDelaySeconds` (2 s). Timeouts and other 5xx are
  retried once for idempotent calls (GET/PATCH) only: **a create is never repeated**, because Graph may already
  have created the contact. Longer throttling reaches the app as `429 GRAPH_RATE_LIMITED` with `Retry-After`.
- Errors: `GRAPH_AUTHORIZATION_FAILED` (403: consent, Conditional Access, no Exchange Online mailbox, missing
  credential), `GRAPH_RATE_LIMITED` (429), `GRAPH_UNAVAILABLE` (503), `GRAPH_CONTACT_CREATE_FAILED` (502),
  `CONTACT_NOT_FOUND` (404).
- Logs: operation, status, attempts, `GraphDurationMs`, `client-request-id` (quote it to Microsoft support) and
  Graph's `error.code`. Never contact data or Graph error messages.

| Setting | Default | Purpose |
|---|---|---|
| `MicrosoftGraph:BaseUrl` | `https://graph.microsoft.com/v1.0/` | Change only for national clouds |
| `MicrosoftGraph:Scopes` | `["https://graph.microsoft.com/Contacts.ReadWrite"]` | Delegated scopes requested OBO |
| `MicrosoftGraph:TimeoutSeconds` | `10` | Per request |
| `MicrosoftGraph:MaxInlineRetryDelaySeconds` | `2` | Longest `Retry-After` honoured inline |

## Self-hosted LLM

Phi-4-mini-instruct on llama.cpp `llama-server`, in `inference/`. See [inference/README.md](inference/README.md)
for setup, sizing, network placement, model provenance and operations. In short:

```bash
cd inference
bash scripts/download-model.sh && bash scripts/new-api-key.sh
docker compose up -d                 # GPU: add -f docker-compose.gpu.yml
bash scripts/smoke-test.sh
```

> **Latency:** a GPU host is expected to meet the 1–2 s preview target; a CPU-only host is expected to take
> ~2–5 s per request. Measure with the smoke test before choosing hardware.

## Mobile sign-in

- Authorization code + PKCE in the system browser (`ASWebAuthenticationSession` on iOS, Custom Tabs on Android)
  via `flutter_appauth`. The app never sees the user's password. Every interactive sign-in shows the account
  picker, so a shared device never silently continues as the previous user.
- The app requests only `openid profile offline_access api://<api-client-id>/access_as_user`. It never holds a
  Graph token.
- **Refresh token**: stored in secure storage (iOS Keychain `first_unlock_this_device`, never synced to iCloud;
  Android Keystore-backed AES-GCM). **Access token**: memory only, refreshed 2 minutes before expiry; concurrent
  requests share one refresh.
- On start, the stored session is validated with one silent refresh. If the device is offline the user stays
  signed in; if Entra says the session is over (`invalid_grant`, `interaction_required`, …) the user is sent to
  login with an explanation.
- A `401` from the API triggers one forced token refresh and a single retry; a second `401` signs the user out.
- **Sign out** clears tokens on the device. It does not end the browser's Entra session; the account picker on
  the next sign-in prevents accidental reuse. Use Intune/MDM device wipe for lost devices.
- All sign-in code is in `lib/features/authentication/data/auth_service.dart`. If Conditional Access requires
  a broker, only that class changes.

## Card capture

- **Scan Business Card** opens the system camera; **Choose from Gallery** opens the system photo picker
  (`image_picker`). Images come back downscaled to 2048 px (JPEG q90) with metadata stripped (no EXIF/GPS).
- The picked file is a **temporary copy** in the app's cache. It is deleted on *Retake*, on leaving the scan
  screen, and when the scan controller is disposed. Deletion is restricted to files inside the app's temp
  directory, so a user's original gallery photo can never be removed.
- If Android kills the app while the camera is open, the photo is recovered on the next launch
  (`retrieveLostData`) instead of forcing a retake.
- Permission denial shows a message pointing to *Settings → Business Cards*.

## On-device OCR

- Google ML Kit Text Recognition v2, **Latin script** (covers Turkish Ç Ğ İ Ö Ş Ü). The model is bundled
  in the app; recognition works offline and the image is never uploaded.
- One recognizer instance lives for the app's lifetime, so the model stays loaded between scans.
- Output is mapped straight into app types (`OcrResult` / `OcrLine`): text, upright-image bounding box,
  ML Kit block and line index, and per-line confidence (Android only; iOS reports none).
- The image is **deleted as soon as recognition succeeds** (or finds no text). It is kept only after a
  recognizer error, so *Try again* can re-run without a new photo, and is deleted when the user leaves.
- No text found → the user is asked to retake (card filling the frame, good light, no glare).
- Debug builds show OCR duration on the result screen.

## Layout features

After OCR the app turns each line's bounding box into a few categorical hints. Only these, never coordinates,
are sent to the API:

```json
{ "text": "ACME TECHNOLOGY", "order": 1, "region": "top", "size": "large",
  "align": "center", "sameBlock": false, "gap": "none" }
```

Rules and thresholds: `ARCHITECTURE.md` → D3, code: `lib/features/scan_card/domain/layout_extractor.dart`
(pure Dart, unit-tested). Debug builds list each line with its tags on the result screen, which is handy for
checking real cards.

> **Manual: measure on real devices.** Record OCR time on a low-end Android device and an older iPhone
> (e.g. 10 scans each, note p50/p95). Simulators and emulators are not representative.

## Contact preview (mobile)

As soon as on-device OCR finishes, the layout lines are sent to `POST /api/business-cards/parse` and the
scan screen is replaced by **Review contact**:

- All 15 fields are editable, grouped as Person / Work / Contact / Address / Notes. The display name is derived
  from first + last name (or the company).
- Fields with `needsReview` get an outlined highlight and **Please check**; editing a field clears its
  highlight. The confidence score is behind the ⓘ icon, so the form stays uncluttered.
- When `meta.fallbackUsed` is true a banner explains that AI assistance was unavailable and fields came from
  the card layout.
- Additional emails and phones are listed under *Also found on the card*, pre-selected, and can be deselected.
- Client-side validation (email, phone, URL, length, control characters, at least one of name / company /
  email / phone) disables **Save** with an explanation. The API validates again on save.
- **Save to Outlook** shows *Checking for duplicates…*, then either the duplicate dialog or *Saving…* (one request
  at a time; back is blocked so the user always learns the outcome), then **"Contact saved to Outlook."** (with
  *The existing contact was updated.* after an update). On failure the message appears above the button; the button
  becomes **Try again** only for retryable errors.
- On failure: **Try again** only for retryable errors (network, timeout, 429, 5xx); **Enter manually** always.
  The trace id is shown for support. The OCR text is kept in memory for the retry and dropped on leaving.
- Debug builds show *OCR → preview* time and the LLM status at the top of the form.

## Changing the app identifier

The placeholder ID `com.example.businesscardscanner` is also the OAuth redirect scheme. URI schemes cannot
contain `_`. To use your organisation's ID, change it in **all** of these places together:

| Where | What |
|---|---|
| `mobile/android/app/build.gradle.kts` | `namespace`, `applicationId`, `manifestPlaceholders["appAuthRedirectScheme"]` |
| `mobile/android/app/src/main/kotlin/…/MainActivity.kt` | `package` line and folder path |
| `mobile/ios/Runner.xcodeproj/project.pbxproj` | `PRODUCT_BUNDLE_IDENTIFIER` (or Xcode → Runner → Signing & Capabilities) |
| `mobile/ios/Runner/Info.plist` | `CFBundleURLSchemes` entry |
| `dart_defines.json` | `ENTRA_REDIRECT_URI` |
| Entra mobile registration | Redirect URI |

## iOS configuration

> **Manual (macOS + Apple Developer account):** open `mobile/ios/Runner.xcworkspace`, select the *Runner* target →
> *Signing & Capabilities*, pick your team, and confirm the bundle ID. No Keychain Sharing entitlement is required.

- `Info.plist` registers the redirect scheme (`CFBundleURLTypes`) and `NSAllowsLocalNetworking` so the
  simulator can call a plain-HTTP local API. Public hosts still require HTTPS (App Transport Security).
- `NSCameraUsageDescription` / `NSPhotoLibraryUsageDescription` provide the permission-prompt texts; adjust the
  wording (and localize to Turkish if needed) before App Store submission. The iOS **simulator has no camera**:
  test *Scan* on a device and use *Gallery* in the simulator.
- Minimum iOS version: **15.5** (required by Google ML Kit). Set in both `ios/Podfile` and the Xcode project.
- ML Kit is distributed as CocoaPods. `ios/Podfile` is committed (Flutter's template + the 15.5 platform); run
  `flutter build ios` or `cd ios && pod install` on a Mac with CocoaPods installed.
- ML Kit ships no 32-bit slices; all devices supported by iOS 15.5 are 64-bit, so no extra exclusions are needed.

## Android configuration

- `manifestPlaceholders["appAuthRedirectScheme"]` registers the redirect activity for AppAuth.
- **Backups disabled** (`allowBackup=false`, `data_extraction_rules.xml`): tokens are never copied to Google Drive
  or to a new device. A restored Keystore-encrypted value could not be decrypted anyway.
- `INTERNET` permission is declared in the main manifest; Flutter's template only adds it to debug builds.
- **No `CAMERA` or storage permission** is declared: capture uses the system camera intent and the gallery uses
  the system Photo Picker. Do not add `CAMERA` to the manifest unless you switch to an in-app camera: once
  declared, Android requires a runtime grant even for the capture intent.
- The bundled Latin text-recognition model adds a few MB per ABI to the APK. Ship an **App Bundle**
  (`flutter build appbundle`) so each device downloads only its own ABI.
- Debug builds allow cleartext HTTP to `10.0.2.2`/`localhost` only (`src/debug/res/xml/network_security_config.xml`).
  Release builds are HTTPS-only.

## Release builds

> **Manual.** Signing keys, store accounts and the final app ID are yours to create; nothing here is automated.

**Android**

1. Create an upload keystore once and keep it (and its passwords) in your secret store. The command is in
   `mobile/android/key.properties.example`. Enable **Play App Signing** so a lost upload key can be reset.
2. Copy `key.properties.example` to `mobile/android/key.properties` (git-ignored, as is `*.jks`) and fill it in.
   `app/build.gradle.kts` uses it for release builds and **falls back to the debug key when it is missing**, so a
   release build without it installs locally but must not be distributed.
3. Build: `flutter build appbundle --release --dart-define-from-file=dart_defines.prod.json`
   (`API_BASE_URL` must be `https://`).
4. CI: write `key.properties` and the keystore from pipeline secrets at build time; never commit them.

**iOS** (macOS): pick the team and bundle ID in Xcode (see *iOS configuration*), then
`flutter build ipa --release --dart-define-from-file=dart_defines.prod.json` and upload with Xcode Organizer or
Transporter. The Entra redirect scheme must match the final bundle ID (*Changing the app identifier*).

## Tests

| Suite | Command |
|---|---|
| Backend unit + integration | `cd backend && dotnet test` |
| Flutter | `cd mobile && flutter test` |
| LLM smoke test (real model, manual) | `bash inference/scripts/smoke-test.sh` |

Automated tests never call the real Microsoft Graph or the real LLM. There is no model benchmark harness (it
was descoped); measure accuracy and latency on real cards before go-live.

## Deployment (Docker)

The image contains only the API. The build context is the **repository root** (the API embeds
`inference/contract/*`):

```bash
docker build -f backend/Dockerfile -t businesscard-api:<version> .
```

- Multi-stage: `sdk:8.0` restores and publishes Release, the runtime is `aspnet:8.0` (Debian, with ICU for
  Turkish casing), runs as the non-root `app` user and listens on **8080** (HTTP).
- `.dockerignore` keeps `mobile/`, tests, models, `.env`, `secrets/`, `appsettings.Development.json` and key
  files out of the build context.
- For production, **pin base images by digest** (`mcr.microsoft.com/dotnet/aspnet:8.0@sha256:…`) and rebuild
  monthly for .NET security patches.

**Single host (API + LLM):**

```bash
cp deploy/.env.example deploy/.env     # fill in Entra IDs, OBO credential, LLM key
docker compose -f deploy/docker-compose.yml up -d --build
```

`deploy/docker-compose.yml` includes `inference/docker-compose.yml`; the API reaches the model at `http://llm:8080`
on the private compose network. The API port is bound to `127.0.0.1:5080` by default (`API_BIND`/`API_PORT`).
The container runs read-only with all capabilities dropped and a 512 MB memory limit. There is no `depends_on`
on the model: the API serves deterministic results until the model is up.

**In front of the API you need a TLS-terminating reverse proxy or ingress** (nginx, Caddy, Azure Application
Gateway, Front Door…):

- Forward `X-Forwarded-For` and `X-Forwarded-Proto`, and set `ForwardedHeaders__KnownNetworks__0` to the proxy's
  CIDR. Headers from any other source are ignored, so a client cannot spoof its scheme or address.
- The API sends HSTS on HTTPS responses outside Development. Inside the container it has no HTTPS port, so it never
  redirects plain-HTTP probe traffic.
- Allow request bodies of at least 256 KB, and use a proxy timeout of ≥ 30 s (Graph calls can take up to ~20 s
  with OBO and one retry).

**Probes:** liveness `GET /health`, readiness `GET /health/ready` (200 also when `Degraded`). The image has no
`HEALTHCHECK` (the runtime image has no curl); configure probes in the orchestrator or proxy.

**Secrets:** `deploy/.env` is git-ignored. Prefer Key Vault for the OBO certificate (managed identity) and your
platform's secret mechanism for `Ai__ApiKey`. The same image runs on Azure Container Apps / App Service for
Containers with the same environment variables; there the model must be reachable on a private network.

**Logs** go to stdout as compact JSON (one event per line), ready for any collector. Docker keeps 5 × 10 MB per
container.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `NETSDK1045` / SDK not found | Install the .NET 8 runtime and any SDK ≥ 8.0.100 |
| Android emulator can't reach the API | Use `10.0.2.2`, not `localhost`; run the API with the `http` profile |
| `Failed to determine the https port for redirect` warning | Expected in the container (HTTP-only behind a TLS proxy); requests are not redirected |
| No HSTS header / request scheme shows `http` behind the proxy | `ForwardedHeaders__KnownNetworks__0` does not contain the proxy's address, or the proxy doesn't send `X-Forwarded-*` |
| `docker compose` error about `include` | Compose older than 2.20; upgrade Docker |
| Container exits at start: `Missing Entra ID configuration` | `deploy/.env` missing or incomplete |
| Logs are one-line JSON locally | Copy `appsettings.Development.json.example` to `appsettings.Development.json` |
| API won't start: `Missing Entra ID configuration: AzureAd:…` | Set the named keys via `dotnet user-secrets` (see Entra section §3) |
| `401` with a token that looks valid | Decode it at a JWT viewer you trust and check `aud` = API client ID (or `api://…` for v1), `iss` = your tenant, `scp` contains `access_as_user`, `exp` in the future. A token for Graph (`aud` = `00000003-…`) is not an API token |
| `403 FORBIDDEN` | Token lacks `access_as_user`: request scope `api://<api-client-id>/access_as_user` and ensure admin consent was granted |
| Every parse shows `meta.llmStatus: "unavailable"` | Inference service down or `Ai:BaseUrl` wrong; check `/health/ready` and `docker compose ps` in `inference/` |
| `meta.llmStatus: "timeout"` on most scans | Host too slow for `Ai:TimeoutMilliseconds` (typical on CPU-only); see `inference/README.md` → Sizing |
| API won't start: `Ai:BaseUrl must be an absolute http(s) URL` | Set `Ai:BaseUrl`, or `Ai:Provider=Disabled` to run without a model |
| Save fails: *Outlook access is not configured correctly* | API has no (or an expired) client credential: see *API credential for On-Behalf-Of*; logs show `invalid_client` / `client_credentials_missing` |
| Save fails: *needs to be approved again* | Admin consent for `Contacts.ReadWrite` missing on the API registration, or a Conditional Access requirement; grant consent, sign out and in |
| Save fails: *no Exchange Online mailbox* | The account is not licensed for Exchange Online (Graph `MailboxNotEnabledForRESTAPI`) |
| `AADSTS65001` (consent required) during sign-in | Grant admin consent on both registrations |
| `AADSTS50011` (redirect URI mismatch) | The mobile registration's redirect URI must equal `ENTRA_REDIRECT_URI` exactly |
| Browser opens but never returns to the app | Redirect scheme differs between `ENTRA_REDIRECT_URI`, Android `appAuthRedirectScheme` and iOS `CFBundleURLSchemes` |
| App shows *Configuration required* | `dart_defines.json` missing or not passed with `--dart-define-from-file` |
| `pod install` fails: *required a higher minimum deployment target* | Keep `platform :ios, '15.5'` in `ios/Podfile`; run `pod repo update` |
| OCR finds no text on a clear card | Card too small in frame or strong glare; fill the frame. Check the photo isn't a screenshot of a screenshot |
| Android: `Cleartext HTTP traffic not permitted` | Use `10.0.2.2` in debug, or HTTPS; other hosts are blocked by design |
