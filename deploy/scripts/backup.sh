#!/bin/sh
# Daily compressed PostgreSQL backup with retention. Runs inside the `backup` compose service.
# Copy ./backups off the server regularly (e.g. rclone to cloud storage) — a backup on the same disk is not enough.
set -eu
STAMP=$(date -u +%Y%m%d-%H%M%S)
FILE="/backups/leadpilot-${STAMP}.dump"
pg_dump --format=custom --no-owner --file="${FILE}"
gzip -f "${FILE}"
echo "backup written: ${FILE}.gz"
find /backups -name 'leadpilot-*.dump.gz' -mtime +"${BACKUP_RETENTION_DAYS:-14}" -delete
