# LeadPilot AI — Workflows

## 1. Daily business workflow (what you do on your phone)

```mermaid
flowchart LR
    A[Discover<br/>category + cities] --> B[Leads stored<br/>source-recorded]
    B --> C{Pick promising leads}
    C --> D[Analyse<br/>website + AI]
    D --> E[Score 0-100<br/>with breakdown]
    E --> F[Pitch Studio<br/>EN / HI / Hinglish]
    F --> G[Pending approval]
    G -->|you review, edit, approve| H[Send via Gmail]
    H --> I[CRM: Contacted<br/>follow-ups +3/+7/+14d]
    I --> J{Reply?}
    J -->|record reply| K[Interested → Meeting → Proposal → Won]
    J -->|no reply| L[Follow-up draft<br/>→ approval again]
    L --> G
```

1. **Discover** (Discover tab): choose categories (e.g. Coaching institutes), state, cities (`Mumbai, Pune, Nashik, Surat`), filters (has/no website, phone, rating) and count. Tap *Estimate cost*, then *Start*. The job runs on the server; you get a push when it finishes. Or **import a CSV**.
2. **Review leads** (Leads tab): filter by *High priority*, *Has email*, *No website*, *Weak website*, stage or city.
3. **Analyse** (Lead → Analyse): the server inspects the public website (HTTPS, mobile, forms, booking, WhatsApp, SEO, broken links, PageSpeed), creates evidence-based opportunities, asks Claude for a structured assessment, then computes a deterministic score.
4. **Pitch** (Lead → Pitch): pick type and language; the draft references only observed opportunities. Edit and save.
5. **Approve** (Approvals tab): review recipient (with where it was found), subject, full body. *Approve* binds the approval to that exact content; any later edit invalidates it.
6. **Send**: the server re-checks everything (approval, content hash, opt-out list, limits, pause switch, Gmail connection) and sends through Gmail. CRM moves the lead to *Contacted* and schedules follow-ups.
7. **Follow up** (Follow-ups): due reminders arrive as notifications; follow-up drafts land in Approvals. Nothing is sent automatically.
8. **Close**: record replies, meetings, proposals and the deal value; track results in Reports.

## 2. Email safety workflow (server-enforced)

```mermaid
stateDiagram-v2
    [*] --> PendingApproval: draft created (opt-out line appended)
    PendingApproval --> Approved: approve(hash, version) matches
    Approved --> PendingApproval: any edit (approval revoked)
    PendingApproval --> Rejected
    Approved --> Expired: approval older than N hours
    Approved --> Queued: send → all checks pass → outbox row (unique idempotency key)
    Queued --> Sending: worker claims row, re-validates, records attempt BEFORE calling Gmail
    Sending --> Sent: Gmail accepted (message id stored)
    Sending --> Queued: 429/5xx/connect failure (retry with backoff)
    Sending --> Failed: permanent error
    Sending --> OutcomeUnknown: response lost — never auto-resent
    OutcomeUnknown --> Sent: you confirm it is in Gmail Sent
    OutcomeUnknown --> PendingApproval: you confirm it was not sent
```

## 3. System workflow (cloud)

```mermaid
flowchart TB
    subgraph Phone[Android APK]
      UI[Angular + Capacitor]
      Cache[(Offline cache<br/>+ sync queue)]
    end
    subgraph VPS[Cloud VPS — runs 24/7]
      N[Nginx + HTTPS] --> API[ASP.NET Core API]
      API --> DB[(PostgreSQL)]
      W[Worker: Hangfire jobs] --> DB
    end
    UI -- HTTPS + JWT --> N
    W --> GP[Google Places API]
    W --> PS[PageSpeed Insights]
    W --> CL[Claude API]
    W --> GM[Gmail API]
    W --> FCM[Firebase Cloud Messaging] --> UI
```

Background jobs (Hangfire, durable in PostgreSQL, resume after restarts): discovery jobs, website analysis, AI analysis, scoring, email outbox (every minute), follow-up reminders (every 15 min), maintenance & provider-data retention (daily), budget monitor (hourly).

## 4. Development & release workflow

```mermaid
flowchart LR
    Code[Commit / push] --> CI1[Backend CI<br/>build + 92 tests + Docker build]
    Code --> CI2[Android APK<br/>tests → build → sign → GitHub Release]
    CI2 --> Link[Stable APK link<br/>releases/download/android-latest/LeadPilot-AI.apk]
    CI1 --> Deploy[Deploy workflow<br/>manual, approval-gated<br/>backup → docker compose up]
```
