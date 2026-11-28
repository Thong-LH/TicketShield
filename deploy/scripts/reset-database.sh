#!/usr/bin/env bash
set -Eeuo pipefail

CONFIRMATION="${1:-}"
EXPECTED_CONFIRMATION="--confirm=RESET_TICKETSHIELD_DATABASE"

if [[ "${CONFIRMATION}" != "${EXPECTED_CONFIRMATION}" ]]; then
  echo "This permanently resets all four PostgreSQL databases." >&2
  echo "Run again with ${EXPECTED_CONFIRMATION}" >&2
  exit 2
fi

CONFIG_FILE="${TICKETSHIELD_DEPLOY_CONFIG:-/etc/ticketshield/deploy.conf}"
if [[ ! -r "${CONFIG_FILE}" ]]; then
  echo "Missing readable deployment config: ${CONFIG_FILE}" >&2
  exit 1
fi

# shellcheck disable=SC1090
source "${CONFIG_FILE}"
cd "${REPO_DIR}"

exec 9>"${APP_ROOT}/.deploy.lock"
if ! flock -n 9; then
  echo "A deployment or database reset is already running." >&2
  exit 1
fi

export DOPPLER_TOKEN
DOPPLER_TOKEN="$(<"${DOPPLER_TOKEN_FILE}")"

compose=(docker compose -f deploy/compose.production.yaml)
doppler=(doppler run -p "${DOPPLER_PROJECT}" -c "${DOPPLER_CONFIG}" --)

if "${doppler[@]}" "${compose[@]}" ps --status running --services | grep -qx postgres; then
  "${doppler[@]}" bash deploy/scripts/backup-postgres.sh
else
  echo "PostgreSQL is not running, so a pre-reset backup cannot be created." >&2
  exit 1
fi

project_name="$(
  "${doppler[@]}" bash -c 'printf %s "${COMPOSE_PROJECT_NAME:-ticketshield}"'
)"
postgres_volume="${project_name}_postgres_data"

"${doppler[@]}" "${compose[@]}" down --remove-orphans

if ! docker volume inspect "${postgres_volume}" >/dev/null 2>&1; then
  echo "Expected PostgreSQL volume not found: ${postgres_volume}" >&2
  exit 1
fi

docker volume rm "${postgres_volume}"
"${doppler[@]}" "${compose[@]}" up -d --remove-orphans

echo "PostgreSQL was reset and the stack was restarted."
echo "The compressed pre-reset backup is in deploy/backups/."

