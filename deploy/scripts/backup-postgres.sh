#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
ENV_FILE="${DEPLOY_DIR}/.env"
COMPOSE_FILE="${DEPLOY_DIR}/compose.production.yaml"
BACKUP_DIR="${BACKUP_DIR:-${DEPLOY_DIR}/backups}"
RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-7}"

mkdir -p "${BACKUP_DIR}"
chmod 700 "${BACKUP_DIR}"

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
target="${BACKUP_DIR}/ticketshield-${timestamp}.sql.gz"

compose=(docker compose)
if [[ -f "${ENV_FILE}" ]]; then
  compose+=(--env-file "${ENV_FILE}")
fi
compose+=(-f "${COMPOSE_FILE}")

"${compose[@]}" exec -T postgres \
  sh -c 'pg_dumpall --clean --if-exists -U "$POSTGRES_USER"' | gzip -9 > "${target}"

chmod 600 "${target}"
find "${BACKUP_DIR}" -type f -name 'ticketshield-*.sql.gz' -mtime "+${RETENTION_DAYS}" -delete
echo "Created ${target}"

