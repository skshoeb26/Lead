#!/usr/bin/env bash
# One-command LeadPilot AI server setup for a fresh Ubuntu 22.04/24.04 VM (Oracle Cloud Always Free, RackNerd, Hetzner...).
#
# Usage (on the server):
#   curl -fsSL https://raw.githubusercontent.com/skshoeb26/Lead/ccr-c4811066-f5sst5/deploy/scripts/server-setup.sh -o setup.sh
#   bash setup.sh <your-domain> <your-email> [git-branch]
# Example:
#   bash setup.sh leadpilot-shoeb.duckdns.org you@gmail.com
#
# What it does: installs Docker, opens ports 80/443 (incl. Oracle's iptables rules), adds swap on small VMs,
# clones the repo, creates deploy/.env with strong random secrets, gets a free HTTPS certificate and starts the stack.
# Email stays in TEST MODE (no real emails) until you change Email__TestMode in deploy/.env.
set -euo pipefail

DOMAIN="${1:-}"; EMAIL="${2:-}"; BRANCH="${3:-ccr-c4811066-f5sst5}"
REPO="https://github.com/skshoeb26/Lead.git"
if [[ -z "$DOMAIN" || -z "$EMAIL" ]]; then
  echo "Usage: bash setup.sh <domain> <email> [branch]"; exit 1
fi

say() { printf "\n\033[1;35m==> %s\033[0m\n" "$*"; }

say "Updating system packages"
sudo apt-get update -y -qq
sudo DEBIAN_FRONTEND=noninteractive apt-get upgrade -y -qq
sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -qq git curl openssl ca-certificates iptables-persistent >/dev/null || \
  sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -qq git curl openssl ca-certificates

say "Opening ports 80 and 443 on the server firewall"
# Oracle Ubuntu images ship restrictive iptables rules; insert ACCEPT rules before the final REJECT.
for port in 80 443; do
  sudo iptables -C INPUT -p tcp --dport "$port" -m state --state NEW -j ACCEPT 2>/dev/null || \
    sudo iptables -I INPUT 5 -p tcp --dport "$port" -m state --state NEW -j ACCEPT
done
command -v netfilter-persistent >/dev/null && sudo netfilter-persistent save >/dev/null || true
command -v ufw >/dev/null && sudo ufw status | grep -q "Status: active" && { sudo ufw allow 80; sudo ufw allow 443; } || true

TOTAL_MB=$(awk '/MemTotal/ {print int($2/1024)}' /proc/meminfo)
if [[ "$TOTAL_MB" -lt 3500 && ! -f /swapfile ]]; then
  say "Adding 2 GB swap (server has ${TOTAL_MB} MB RAM)"
  sudo fallocate -l 2G /swapfile && sudo chmod 600 /swapfile && sudo mkswap /swapfile >/dev/null && sudo swapon /swapfile
  echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab >/dev/null
fi

if ! command -v docker >/dev/null; then
  say "Installing Docker"
  curl -fsSL https://get.docker.com | sudo sh >/dev/null
  sudo usermod -aG docker "$USER"
fi
DOCKER="sudo docker"

say "Getting LeadPilot AI code (branch $BRANCH)"
if [[ -d "$HOME/Lead/.git" ]]; then
  git -C "$HOME/Lead" fetch -q origin "$BRANCH" && git -C "$HOME/Lead" checkout -q "$BRANCH" && git -C "$HOME/Lead" pull -q --ff-only
else
  git clone -q --branch "$BRANCH" "$REPO" "$HOME/Lead"
fi
cd "$HOME/Lead/deploy"
mkdir -p secrets backups && chmod 700 secrets
chmod +x scripts/*.sh

if [[ ! -f .env ]]; then
  say "Creating deploy/.env with random secrets"
  cp .env.example .env
  PG_PASS=$(openssl rand -hex 24)
  JWT_KEY=$(openssl rand -base64 48 | tr -d '\n/+=')
  sed -i "s|^DOMAIN=.*|DOMAIN=${DOMAIN}|" .env
  sed -i "s|^LETSENCRYPT_EMAIL=.*|LETSENCRYPT_EMAIL=${EMAIL}|" .env
  sed -i "s|^POSTGRES_PASSWORD=.*|POSTGRES_PASSWORD=${PG_PASS}|" .env
  sed -i "s|^Jwt__SigningKey=.*|Jwt__SigningKey=${JWT_KEY}|" .env
  sed -i "s|^GoogleOAuth__RedirectUri=.*|GoogleOAuth__RedirectUri=https://${DOMAIN}/api/v1/integrations/google/callback|" .env
  chmod 600 .env
else
  say "deploy/.env already exists — keeping it"
fi

say "Checking that ${DOMAIN} points to this server"
MY_IP=$(curl -fsS https://api.ipify.org || true)
DNS_IP=$(getent hosts "$DOMAIN" | awk '{print $1}' | head -1 || true)
echo "Server public IP: ${MY_IP:-unknown}   ${DOMAIN} resolves to: ${DNS_IP:-nothing}"
if [[ -n "$MY_IP" && "$MY_IP" != "$DNS_IP" ]]; then
  echo "WARNING: ${DOMAIN} does not point to ${MY_IP} yet. Update it (e.g. on duckdns.org) and re-run this script."
  exit 1
fi

say "Building and starting (first build takes 5-10 minutes)"
$DOCKER compose build
# shellcheck disable=SC1091
set -a; . ./.env; set +a
if ! $DOCKER compose run --rm --entrypoint sh certbot -c "test -f /etc/letsencrypt/renewal/${DOMAIN}.conf" 2>/dev/null; then
  say "Getting free HTTPS certificate (Let's Encrypt)"
  sudo bash scripts/init-letsencrypt.sh
fi
$DOCKER compose up -d

say "Waiting for the API to start"
for i in $(seq 1 60); do
  if curl -fsS "https://${DOMAIN}/health" >/dev/null 2>&1; then
    printf "\n\033[1;32mLeadPilot AI is live at https://%s\033[0m\n" "$DOMAIN"
    echo "Open the app → Server → https://${DOMAIN} → 'First time? Set up owner account'."
    echo "Add API keys later in ~/Lead/deploy/.env, then run: cd ~/Lead/deploy && sudo docker compose up -d"
    exit 0
  fi
  sleep 5
done
echo "API did not respond yet. Check logs with: cd ~/Lead/deploy && sudo docker compose logs --tail 100 api"
exit 1
