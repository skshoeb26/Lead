#!/bin/sh
# Restore a backup into the running stack. DESTRUCTIVE: replaces the current database contents.
# Usage (from deploy/):  ./scripts/restore.sh backups/leadpilot-20260101-020000.dump.gz
set -eu
FILE="${1:?usage: restore.sh <backup.dump.gz>}"
printf "This will REPLACE the current database with %s. Type 'restore' to continue: " "$FILE"
read -r answer
[ "$answer" = "restore" ] || { echo "aborted"; exit 1; }
docker compose stop api worker
gunzip -c "$FILE" | docker compose exec -T postgres sh -c 'pg_restore --clean --if-exists --no-owner -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
docker compose start api worker
echo "restore complete"
