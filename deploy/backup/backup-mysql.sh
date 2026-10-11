#!/usr/bin/env bash
# Nightly MySQL backup for the Doggo compose stack.
# Keeps 7 daily and 8 weekly dumps under $BACKUP_DIR.
#
# Install (as root):
#   cp deploy/backup/backup-mysql.sh /usr/local/bin/doggo-backup.sh
#   chmod 700 /usr/local/bin/doggo-backup.sh
#   crontab -e
#   17 3 * * * /usr/local/bin/doggo-backup.sh >> /var/log/doggo-backup.log 2>&1
#
# Restore a dump:
#   docker exec -i doggo-mysql mysql -uroot -p"$MYSQL_ROOT_PASSWORD" doggo < dump.sql
set -euo pipefail

BACKUP_DIR="${BACKUP_DIR:-/var/backups/doggo}"
CONTAINER="${CONTAINER:-doggo-mysql}"
MYSQL_ROOT_PASSWORD="${MYSQL_ROOT_PASSWORD:?export MYSQL_ROOT_PASSWORD first}"
DAILY_KEEP=7
WEEKLY_KEEP=8

umask 077
mkdir -p "$BACKUP_DIR/daily" "$BACKUP_DIR/weekly"

stamp="$(date +%F_%H%M%S)"
target="$BACKUP_DIR/daily/doggo-$stamp.sql.gz"

# Single transaction dump; routines/events included, binlog position recorded.
docker exec "$CONTAINER" sh -c \
  'exec mysqldump -uroot -p"$MYSQL_ROOT_PASSWORD" --single-transaction --routines --events --databases doggo' \
  | gzip > "$target"

# Sunday also lands in the weekly pool.
if [ "$(date +%u)" = "7" ]; then
  cp "$target" "$BACKUP_DIR/weekly/"
fi

ls -1t "$BACKUP_DIR/daily" | tail -n +$((DAILY_KEEP + 1)) | while read -r old; do
  rm -f "$BACKUP_DIR/daily/$old"
done
ls -1t "$BACKUP_DIR/weekly" | tail -n +$((WEEKLY_KEEP + 1)) | while read -r old; do
  rm -f "$BACKUP_DIR/weekly/$old"
done

echo "backup written: $target ($(du -h "$target" | cut -f1))"
