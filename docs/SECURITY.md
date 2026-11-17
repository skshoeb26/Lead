# Security & Compliance Checklist

## Implemented
| Area | Control |
|---|---|
| Passwords | PBKDF2 (ASP.NET Identity hasher, 210k iterations), complexity rules, lockout after 5 failures (15 min) |
| Sessions | JWT access tokens (15 min, issuer/audience/lifetime validated); refresh tokens 30 days, stored as SHA-256 hashes, **rotated on every use**, reuse detection revokes the whole token family; logout and password change revoke tokens |
| Registration | Only for the first (owner) account unless `Auth__AllowRegistration=true` |
| Authorisation | Fallback policy requires authentication on every endpoint; settings require the Owner/Admin role |
| Rate limiting | Global per user/IP; stricter for auth (10/min) and expensive endpoints (discovery, AI, analysis) — all configurable |
| Input validation | FluentValidation + service-level checks; EF Core parameterised queries only |
| Transport | HTTPS via Nginx + Let's Encrypt, HSTS; release APK network config forbids cleartext; app refuses non-HTTPS server URLs in production builds |
| Headers | nosniff, X-Frame-Options DENY, no-referrer, strict CSP on API responses, CORS allow-list |
| Secrets | Environment variables only (`deploy/.env`, git-ignored); `.env.example` has placeholders; signing keystores and google-services.json come from CI secrets; nothing secret is committed |
| OAuth tokens | Gmail refresh tokens + PKCE verifiers encrypted with ASP.NET Data Protection (keys on a separate volume); never returned to the app; access tokens in memory only |
| Mobile storage | Refresh token in Android Keystore-backed secure storage; access token in memory; cached prospect data and offline queue removed on logout; Android backup/device-transfer disabled |
| SSRF | URL validation (http/https, ports 80/443, no credentials/internal names), DNS resolution checked at connect time (blocks private, loopback, link-local/metadata, CGNAT, ULA, multicast), manual redirect validation (max 5), timeouts, 2 MB response cap, no script execution, robots.txt respected, never submits forms |
| Email safety | No send without approval; approval bound to content hash + version + recipient; any edit revokes it; approvals expire; server re-validates at queue **and** right before sending; suppression list; source-recorded recipient; hourly/daily limits; global pause; unique idempotency key per approval; attempt recorded before calling Gmail; lost responses become *OutcomeUnknown* and are never auto-resent; test mode on by default |
| Header injection | MIME builder rejects CR/LF in headers; RFC 2047 encoding |
| CSV | 2 MB / 5,000 rows, `.csv` only, per-row validation; export neutralises formula injection |
| Audit | Auth events, lead create/update/delete/merge/erase, approvals, sends, settings and suppression changes; per-email audit trail. Audit logs never contain passwords or tokens |
| Logging | Serilog structured logs; no credentials logged |
| Containers | Non-root runtime user; Postgres not exposed publicly; Hangfire dashboard not exposed |
| Backups | Daily pg_dump with retention; restore script; instructions to copy off-server |

## Your responsibilities before going live
- [ ] Strong unique values for `POSTGRES_PASSWORD` and `Jwt__SigningKey` (`openssl rand -base64 48`)
- [ ] Restrict Google API keys to your VPS IP and required APIs; set billing budgets/quotas
- [ ] Firewall: only 22/80/443 open; SSH key auth only (disable password login)
- [ ] Off-server backups configured and a test restore performed
- [ ] Configure release signing secrets so APK updates install cleanly
- [ ] Keep `Email__TestMode=true` until end-to-end testing is done

## Responsible outreach
LeadPilot is designed for legitimate B2B prospecting: data comes only from approved sources (Google Places API, CSV you supply, manual entry) with provenance recorded per contact; contact verification is split into source / syntax / delivery / consent; opt-outs are honoured across channels; emails include a clear sender identity and an opt-out line; subject lines are generated to be accurate; WhatsApp business-initiated messages require recorded opt-in and only drafts are produced in this release; LinkedIn is drafts-only; prospects can be erased on request.

Human approval is necessary but **does not by itself establish a lawful basis** for processing or contacting someone. Review your obligations under India's Digital Personal Data Protection Act 2023 and rules, TRAI/telecom rules for calls/SMS, IT Act, and each provider's policies (Google, Meta/WhatsApp, LinkedIn). This software is not a legal compliance guarantee.
