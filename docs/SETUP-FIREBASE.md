# Firebase push notifications setup

1. https://console.firebase.google.com → Add project (can reuse the Google Cloud project).
2. Add an **Android app** with package name `com.leadpilot.ai` → download **google-services.json**.
3. Add it to GitHub so the APK build includes it: repository → Settings → Secrets and variables → Actions → New secret `GOOGLE_SERVICES_JSON` = full file contents. (For local builds place it at `mobile/android/app/google-services.json`; it is git-ignored.) The CI build then sets `pushEnabled: true`.
4. Server side: Project settings → Service accounts → **Generate new private key** → save as `deploy/secrets/firebase.json` on the VPS (`chmod 600`). `.env`: `Firebase__ServiceAccountFile=/run/secrets/leadpilot/firebase.json`. Restart api/worker.
5. Install the new APK, sign in, allow notifications. Settings → Notifications shows "registered".

Behaviour
- Notifications for: discovery completed, high-priority leads, analysis completed, drafts awaiting approval, email failures/unknown outcomes, follow-ups due, job errors, budget warnings (each toggleable).
- Previews are generic by default (no lead names or email content); enable *Show lead names in push previews* if you want details.
- Tapping a notification opens the relevant screen (route sent in the data payload).
- Xiaomi/MIUI: enable Autostart and set battery saver to "No restrictions" for LeadPilot, otherwise notifications can be delayed.
