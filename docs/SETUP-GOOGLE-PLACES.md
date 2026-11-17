# Google Places API setup

1. https://console.cloud.google.com → create a project (e.g. `leadpilot`) → enable billing.
2. APIs & Services → Library → enable **Places API (New)** and (optional) **PageSpeed Insights API**.
3. Credentials → Create API key → **Restrict key**: API restrictions = Places API (New) [+ PageSpeed Insights API]; application restriction = IP address of your VPS.
4. Set budgets/alerts in Billing → Budgets & alerts, and per-API quotas (APIs & Services → Places API → Quotas) as a hard cap.
5. Server `.env`: `GooglePlaces__ApiKey=...` (and `PageSpeed__ApiKey=...` if enabled). Restart api/worker.

How LeadPilot uses it
- Only the official **Text Search (New)** endpoint, `regionCode=IN`, with a minimal field mask (id, name, address components, location, website, phone, rating, review count, business status, Maps URL). No Maps scraping.
- Each search request ≈ `GooglePlaces__CostPerRequestUsd` (default $0.035 for this field tier — verify against current Google pricing). The app shows an estimate before every job and blocks jobs above your monthly discovery budget.
- Text Search returns up to 60 results per query (3 pages); a job is split into segments (category × city) to cover more.
- **Data retention**: place IDs may be stored indefinitely; other Places content is subject to Google's caching limits. The daily maintenance job clears (or, with `GooglePlaces__RefreshContentOnExpiry=true`, refreshes) cached Places attributes on unengaged leads after `GooglePlaces__ContentRetentionDays` (default 30). Review Google Maps Platform Terms for your use and attribution ("© Google Maps" is stored with each source).
