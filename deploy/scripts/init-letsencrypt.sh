#!/bin/sh
# First-time HTTPS certificate issuance for ${DOMAIN}. Run once from deploy/ after DNS points to this server.
set -eu
. ./.env
: "${DOMAIN:?DOMAIN missing in .env}" "${LETSENCRYPT_EMAIL:?LETSENCRYPT_EMAIL missing in .env}"

# 1. Temporary self-signed cert so nginx can start.
docker compose run --rm --entrypoint sh certbot -c "mkdir -p /etc/letsencrypt/live/${DOMAIN} && \
  openssl req -x509 -nodes -newkey rsa:2048 -days 1 -subj '/CN=localhost' \
  -keyout /etc/letsencrypt/live/${DOMAIN}/privkey.pem -out /etc/letsencrypt/live/${DOMAIN}/fullchain.pem"
docker compose up -d nginx

# 2. Replace with a real Let's Encrypt certificate.
docker compose run --rm --entrypoint sh certbot -c "rm -rf /etc/letsencrypt/live/${DOMAIN} /etc/letsencrypt/archive/${DOMAIN} /etc/letsencrypt/renewal/${DOMAIN}.conf"
docker compose run --rm certbot certonly --webroot -w /var/www/certbot -d "${DOMAIN}" --email "${LETSENCRYPT_EMAIL}" --agree-tos --no-eff-email
docker compose exec nginx nginx -s reload
echo "HTTPS ready at https://${DOMAIN}"
