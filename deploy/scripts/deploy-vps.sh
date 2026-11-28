#!/usr/bin/env bash
set -Eeuo pipefail

trap 'echo "Deployment failed at line ${LINENO}." >&2' ERR

CONFIG_FILE="${TICKETSHIELD_DEPLOY_CONFIG:-/etc/ticketshield/deploy.conf}"
if [[ ! -r "${CONFIG_FILE}" ]]; then
  echo "Missing readable deployment config: ${CONFIG_FILE}" >&2
  exit 1
fi

# shellcheck disable=SC1090
source "${CONFIG_FILE}"

: "${REPO_DIR:?Missing REPO_DIR}"
: "${REPO_BRANCH:?Missing REPO_BRANCH}"
: "${DOPPLER_PROJECT:?Missing DOPPLER_PROJECT}"
: "${DOPPLER_CONFIG:?Missing DOPPLER_CONFIG}"
: "${DOPPLER_TOKEN_FILE:?Missing DOPPLER_TOKEN_FILE}"

if [[ ! -r "${DOPPLER_TOKEN_FILE}" ]]; then
  echo "Missing readable Doppler service token: ${DOPPLER_TOKEN_FILE}" >&2
  exit 1
fi

exec 9>"${APP_ROOT}/.deploy.lock"
if ! flock -n 9; then
  echo "Another TicketShield deployment is already running." >&2
  exit 1
fi

cd "${REPO_DIR}"
if [[ -n "$(git status --porcelain)" ]]; then
  echo "The VPS repository has local changes; refusing to overwrite them." >&2
  exit 1
fi

git fetch origin "${REPO_BRANCH}"
git checkout "${REPO_BRANCH}"
git pull --ff-only origin "${REPO_BRANCH}"

export DOPPLER_TOKEN
DOPPLER_TOKEN="$(<"${DOPPLER_TOKEN_FILE}")"

doppler run -p "${DOPPLER_PROJECT}" -c "${DOPPLER_CONFIG}" -- \
  bash deploy/scripts/preflight.sh

if doppler run -p "${DOPPLER_PROJECT}" -c "${DOPPLER_CONFIG}" -- \
  docker compose -f deploy/compose.production.yaml ps --status running --services \
  | grep -qx postgres; then
  doppler run -p "${DOPPLER_PROJECT}" -c "${DOPPLER_CONFIG}" -- \
    bash deploy/scripts/backup-postgres.sh
fi

doppler run -p "${DOPPLER_PROJECT}" -c "${DOPPLER_CONFIG}" -- \
  docker compose -f deploy/compose.production.yaml build --pull

doppler run -p "${DOPPLER_PROJECT}" -c "${DOPPLER_CONFIG}" -- \
  docker compose -f deploy/compose.production.yaml up -d --remove-orphans

expected_services=(
  caddy postgres rabbitmq redis mock-organizer-api identity-api core-api
  settlement-api gateway anti-bot-gateway
)

deadline=$((SECONDS + 300))
while (( SECONDS < deadline )); do
  mapfile -t running_services < <(
    doppler run -p "${DOPPLER_PROJECT}" -c "${DOPPLER_CONFIG}" -- \
      docker compose -f deploy/compose.production.yaml ps --status running --services
  )

  missing=0
  for service in "${expected_services[@]}"; do
    if ! printf '%s\n' "${running_services[@]}" | grep -qx "${service}"; then
      missing=1
      break
    fi
  done

  if (( missing == 0 )); then
    break
  fi
  sleep 5
done

if (( missing != 0 )); then
  doppler run -p "${DOPPLER_PROJECT}" -c "${DOPPLER_CONFIG}" -- \
    docker compose -f deploy/compose.production.yaml ps
  echo "Not all services reached the running state within 5 minutes." >&2
  exit 1
fi

doppler run -p "${DOPPLER_PROJECT}" -c "${DOPPLER_CONFIG}" -- \
  docker compose -f deploy/compose.production.yaml ps

echo "Deployment completed at commit $(git rev-parse --short HEAD)."

