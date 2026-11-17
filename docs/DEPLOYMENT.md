# Production Deployment

The backend (API + Worker + PostgreSQL) runs on a small cloud VPS so discovery, AI analysis, the email outbox and reminders keep running when your laptop is off. The APK talks to it over HTTPS.

> Nothing here has been purchased or deployed for you. Choosing a provider, buying a domain and deploying publicly are your decisions.

## 1. Hosting options (small single-owner workload)

Requirements: 2 vCPU, 2–4 GB RAM, 40 GB disk, Ubuntu 24.04, Docker. Prices are indicative — check current pricing before buying.

| Option | Approx. cost / month | Notes |
|---|---|---|
| **Hetzner Cloud CX22/CPX21** (EU/US/SG) | ~€4–8 | Best price/performance; Singapore region for lower India latency on some plans. |
| **DigitalOcean Basic Droplet** (Bangalore region) | ~$12–24 | India region, simple UI, managed backups add-on (+20%). |
| **AWS Lightsail** (Mumbai) | ~$10–20 | India region, predictable bundle pricing, snapshots. |
| **Oracle Cloud Always Free (Ampere, Mumbai/Hyderabad)** | $0 | Generous free tier (ARM). Capacity can be hard to obtain; good for testing. |
| **Azure B2s / App Service** (Central India) | ~$30+ | Natural for .NET, but more expensive for this size. |

Recommendation: **DigitalOcean Bangalore or AWS Lightsail Mumbai** for latency and simplicity; **Hetzner** if cost matters most. A domain (e.g. `.in` ~₹500–900/year) is required for HTTPS.

## 2. Server setup (once)

```bash
# On the VPS (Ubuntu 24.04)
sudo apt update && sudo apt -y upgrade
sudo apt -y install ca-certificates curl git ufw
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker $USER && newgrp docker
sudo ufw allow OpenSSH && sudo ufw allow 80 && sudo ufw allow 443 && sudo ufw --force enable

git clone https://github.com/skshoeb26/Lead.git && cd Lead/deploy
cp .env.example .env && nano .env          # fill in DOMAIN, passwords, keys (see docs/SETUP-*.md)
mkdir -p secrets backups && chmod 700 secrets
# optional: put the Firebase service-account JSON at deploy/secrets/firebase.json (chmod 600)
```

Point an **A record** for your API domain (e.g. `api.yourdomain.in`) to the VPS IP, then:

```bash
chmod +x scripts/*.sh
./scripts/init-letsencrypt.sh             # obtains the HTTPS certificate (auto-renews via the certbot service)
docker compose up -d --build               # postgres, api, worker, nginx, certbot, backup
docker compose ps && curl https://api.yourdomain.in/health
```

On first launch, open the app → *Server* → `https://api.yourdomain.in` → *First time? Set up owner account*. Registration closes automatically afterwards.

## 3. Going live checklist

1. App works end-to-end with `Email__TestMode=true` (default).
2. Gmail connected (Settings → Integrations) and Google OAuth consent screen configured (docs/SETUP-GMAIL.md).
3. Your profile, services, signature and send limits set (Settings).
4. Then set `Email__TestMode=false` in `deploy/.env` and `docker compose up -d api worker`.

## 4. Updates / CI-CD

- **Backend CI** builds, runs tests and builds Docker images on every push.
- **Deploy (manual)**: Actions → *Deploy backend (manual)* → choose ref. It SSHes to the VPS, takes a backup, then rebuilds `api` and `worker`. Configure a GitHub *production* environment with required reviewers and secrets `VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY`.
- Manual alternative on the VPS: `git pull && cd deploy && docker compose up -d --build api worker`.

## 5. Backup & recovery

- The `backup` service writes `deploy/backups/leadpilot-YYYYMMDD-HHMMSS.dump.gz` daily and keeps `BACKUP_RETENTION_DAYS` (default 14).
- **Copy backups off the server** (a backup on the same disk does not survive disk loss), e.g. with rclone to Google Drive/S3 via a daily cron:
  `0 3 * * * rclone copy ~/Lead/deploy/backups remote:leadpilot-backups --max-age 48h`
- Also back up the **Data Protection keys volume** (`dpkeys`) — without it, stored Gmail tokens can't be decrypted (you would just reconnect Gmail):
  `docker run --rm -v leadpilot_dpkeys:/k -v $PWD/backups:/b alpine tar czf /b/dpkeys-$(date +%F).tgz -C /k .`
- Enable your provider's VM snapshots as a second layer.

**Restore** (destructive — replaces current data):
```bash
cd ~/Lead/deploy
./scripts/restore.sh backups/leadpilot-20260101-020000.dump.gz
```
Test a restore on a separate VM at least once.

## 6. Operations

- Logs: `docker compose logs -f api worker`
- Hangfire dashboard is not exposed publicly. Job state is visible in the app (Discovery jobs, Approvals, Notifications).
- Rotate secrets: change `.env`, `docker compose up -d`. Changing `Jwt__SigningKey` signs everyone out.
- Resource use: Postgres + API + Worker idle at ~400–600 MB RAM.
