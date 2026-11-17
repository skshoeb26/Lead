# LeadPilot AI

AI-powered client-acquisition platform for an independent software developer in India: discover businesses, analyse their websites for genuine opportunities, score leads transparently, generate personalised pitches, send **only human-approved** emails through Gmail, and run the whole sales pipeline from an Android phone — while a cloud backend does the work 24/7.

| Part | Tech | Folder |
|---|---|---|
| Backend API + Worker | .NET 10, ASP.NET Core, EF Core, PostgreSQL, Hangfire | `backend/` |
| Android app | Angular 22, Angular Material, Capacitor 8 | `mobile/` |
| Deployment | Docker Compose, Nginx, Let's Encrypt, backups | `deploy/` |
| CI/CD | GitHub Actions (backend CI, APK release, manual deploy) | `.github/workflows/` |

**Download the APK:** `https://github.com/skshoeb26/Lead/releases/download/android-latest/LeadPilot-AI.apk` (built by the *Android APK* workflow on every push to `mobile/`).

## Documentation

- [Workflows](docs/WORKFLOW.md) — daily use, email safety state machine, system and release flow
- [Architecture, database (ER diagram) & API](docs/ARCHITECTURE.md)
- [Local development on Windows](docs/LOCAL-DEV-WINDOWS.md)
- [Production deployment, hosting comparison, backup & restore](docs/DEPLOYMENT.md)
- Integrations: [Claude](docs/SETUP-CLAUDE.md) · [Google Places](docs/SETUP-GOOGLE-PLACES.md) · [Gmail OAuth](docs/SETUP-GMAIL.md) · [Firebase push](docs/SETUP-FIREBASE.md)
- [Security & compliance checklist](docs/SECURITY.md)
- [Progress report, test results, known limitations, TODO](docs/PROGRESS.md)
- Environment variables: [`deploy/.env.example`](deploy/.env.example)

## Quick start (development)

```bash
# backend (needs PostgreSQL; see docs/LOCAL-DEV-WINDOWS.md)
cd backend && dotnet run --project src/LeadPilot.API        # http://localhost:5054/swagger
# app in a browser
cd mobile && npm ci && npm start                            # http://localhost:4200, set server URL http://localhost:5054
# tests
cd backend && dotnet test        # 92 tests
cd mobile && npm run test:ci     # 10 tests
```

In Development, missing API keys fall back to clearly labelled **demo/mock** providers (`[DEMO]` businesses, `[MOCK]` AI). These are never registered in Production. Email is in **test mode** by default everywhere — no real email is sent until you set `Email__TestMode=false`.
