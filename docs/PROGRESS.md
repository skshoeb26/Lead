# Progress Report

Status as of this delivery. "Working" = implemented and exercised by tests and/or an end-to-end run; "Awaiting credentials" = real integration code exists and is wired, but could not be run against the live service from the build environment.

## Phase completion

| Phase | Status | Notes |
|---|---|---|
| 1 Foundation | ✅ Working | .NET 10 solution (6 projects), PostgreSQL + EF Core migration, JWT + refresh rotation, Angular 22 + Capacitor 8 app |
| 2 Lead management | ✅ Working | CRUD, categories (14 seeded, user-editable), CSV import/export, duplicate detection + merge, filters/sort, provenance per contact |
| 3 Lead discovery | ✅ Working with demo provider · ⏳ Google Places awaiting API key | Resumable/pausable/cancellable jobs, multi-city segments, cost estimate + budget guard, rate limiting, retention job |
| 4 Website analysis | ✅ Working | SSRF-safe analyzer with 18 evidence checks, robots.txt, broken links, rule-based opportunities; PageSpeed client (works keyless with low quota; key recommended) |
| 5 AI intelligence | ✅ Working with mock · ⏳ Claude awaiting API key | Official SDK, structured outputs, validation, versioned prompts, usage/cost tracking, budget pause; deterministic scoring with breakdown |
| 6 Pitch Studio | ✅ Working with mock · ⏳ live AI awaiting key | 9 draft types, English/Hindi/Hinglish, evidence-only prompts, edit & save, WhatsApp/LinkedIn drafts |
| 7 Gmail outreach | ✅ Approval/outbox working (test mode) · ⏳ Gmail OAuth awaiting client credentials | PKCE + state, encrypted tokens, hash-bound approvals, durable outbox, idempotency, unknown-outcome reconciliation, audit |
| 8 CRM | ✅ Working | 14-stage pipeline (kanban + table), notes/calls/replies, follow-ups (+3/+7/+14), meetings, proposals, deals, reports with explained rates |
| 9 Notifications & offline | ✅ In-app working · ⏳ push awaiting Firebase config | FCM HTTP v1 sender, device registration, deep links, privacy-safe previews; offline cache + safe-change queue |
| 10 Cloud deployment | ✅ Ready · ⏸ not deployed (needs your VPS + domain) | Dockerfile, compose (Postgres, API, Worker, Nginx, Certbot, backups), scripts, CI/CD |

End-to-end run performed locally: login → discovery job (12 demo leads, 2 cities) → website + AI analysis → score 87 with breakdown → pitch → draft → approve → edit invalidated approval → re-approve → send (test mode, Gmail not called) → CRM *Contacted* + 3 follow-ups scheduled. The analyzer also correctly **refused** to connect when a hostname resolved to 127.0.0.1 (SSRF guard).

## Test results
- Backend: **92 passed, 0 failed** (`dotnet test`) — unit (scoring, duplicates, normalisation, opportunity rules, AI validation, SSRF, robots, hashing, MIME/header injection) and integration (auth, refresh rotation/reuse, lockout, leads/CSV/merge, approval hash binding, edit invalidation, DB tamper detection, duplicate-send prevention, suppression, pause, retry vs unknown outcome, hourly limit, OAuth state/PKCE/scope, analysis pipeline).
- Frontend: **10 passed** (`npm run test:ci`) — HTTPS URL validation, error mapping, auth interceptor (bearer only to API, single refresh + retry), form patterns, status presentation.
- GitHub Actions: Backend CI green (tests + Docker images). APK workflow builds and publishes the release.

## Known limitations
- Real Google Places, Claude, Gmail and Firebase calls were not executed here (no credentials in the build environment); they are covered by mocks/fakes in tests. Expect minor tuning after first live runs.
- With only `gmail.send`, the server cannot read Gmail: replies must be recorded manually in the CRM, and *OutcomeUnknown* emails are reconciled by you checking the Sent folder.
- Google OAuth apps in *Testing* status issue refresh tokens that expire after ~7 days (reconnect weekly) until the app is verified.
- Website analysis inspects static HTML (no JavaScript rendering), so features rendered only by JS may be reported as "not found on inspected pages" (always phrased with that caveat).
- Business size is not inferred (not reliably available); public business emails are rarely returned by Google Places — CSV/manual entry or the business's contact page are the main email sources (the analyzer does not harvest emails).
- WhatsApp API sending is disabled by design until opt-in collection exists; LinkedIn is drafts only.
- Single owner account today; schema and services carry `OwnerUserId` for future multi-user.
- Without release-signing secrets, CI signs the APK with a temporary debug key: uninstall the old build before installing a new one.
- The Hangfire dashboard is intentionally not exposed.

## TODO / next steps
- [ ] Provide credentials (see questions in the delivery message) and run live smoke tests per integration.
- [ ] Deploy to the chosen VPS; set `LEADPILOT_API_BASE_URL` repository variable so the APK defaults to it.
- [ ] Add release signing secrets for upgradeable APKs.
- [ ] Optional: reply detection via `gmail.readonly` (requires broader scope + Google verification) — not enabled.
- [ ] Optional: contact-page email extraction with explicit source recording; JS rendering for analysis (headless browser in a sandbox).
