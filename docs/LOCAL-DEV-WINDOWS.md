# Local development on Windows

## Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download), Node.js 22 LTS, Git
- PostgreSQL 16 (installer from postgresql.org) **or** Docker Desktop
- Android Studio (for running the APK on a device/emulator) with Android SDK 36 and JDK 21
- VS Code (+ C# Dev Kit, Angular Language Service) or Visual Studio 2026

## Database
```powershell
# Docker option
docker run -d --name leadpilot-pg -p 5432:5432 -e POSTGRES_DB=leadpilot -e POSTGRES_USER=leadpilot -e POSTGRES_PASSWORD=devpassword postgres:16
```
(or create database `leadpilot` / user `leadpilot` / password `devpassword` in pgAdmin — matches `appsettings.Development.json`).

## Backend
```powershell
cd backend
dotnet run --project src/LeadPilot.API          # http://localhost:5054 , Swagger at /swagger
```
Development mode runs Hangfire inside the API (no separate worker needed), applies migrations and seeds categories. Without API keys it uses demo discovery data and a mock AI (clearly labelled). To try real integrations locally, use user-secrets (never commit keys):
```powershell
cd src/LeadPilot.API
dotnet user-secrets init
dotnet user-secrets set "AI:Claude:ApiKey" "sk-ant-..."
dotnet user-secrets set "GooglePlaces:ApiKey" "..."
```
Optional bootstrap owner: `dotnet user-secrets set "Auth:BootstrapOwnerEmail" "you@example.com"` and `"Auth:BootstrapOwnerPassword"`.

Tests: `dotnet test` (92 tests, no external calls).

## App in the browser
```powershell
cd mobile
npm ci
npm start              # http://localhost:4200
```
On the login screen open **Server** and enter `http://localhost:5054`, then *First time? Set up owner account*.

Tests: `npm run test:ci`.

## App on your Xiaomi / emulator
```powershell
cd mobile
npm run cap:sync
npx cap open android        # opens Android Studio → Run
```
- Emulator reaches your PC at `http://10.0.2.2:5054` (default dev server URL; debug builds allow cleartext only to 10.0.2.2/localhost).
- A physical phone needs the backend reachable over **HTTPS** (deploy to the VPS, or use a tunnel such as `cloudflared tunnel --url http://localhost:5054` and enter the https URL in Server settings).
- Xiaomi/MIUI: enable Developer options → USB debugging **and** "Install via USB"; allow autostart for LeadPilot so push notifications arrive.

Build a debug APK from the command line: `npm run android:debug` → `mobile/android/app/build/outputs/apk/debug/app-debug.apk`.
