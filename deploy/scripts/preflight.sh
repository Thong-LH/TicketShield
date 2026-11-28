#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
ENV_FILE="${DEPLOY_DIR}/.env"
COMPOSE_FILE="${DEPLOY_DIR}/compose.production.yaml"

required=(
  API_DOMAIN ORGANIZER_API_DOMAIN ACME_EMAIL CORS_ALLOWED_ORIGINS
  POSTGRES_PASSWORD RABBITMQ_PASSWORD REDIS_PASSWORD JWT_SECRET
  ORGANIZER_API_KEY ORGANIZER_HMAC_KEY SETTLEMENT_SHARED_SECRET
  ANTIBOT_SESSION_SECRET ANTIBOT_ADMIN_API_KEY
)

if [[ -f "${ENV_FILE}" ]]; then
  if grep -Eq '(^|=)(CHANGE_ME|.*\.example\.com)' "${ENV_FILE}"; then
    echo "deploy/.env still contains CHANGE_ME or example.com placeholders." >&2
    exit 1
  fi

  for key in "${required[@]}"; do
    if ! grep -Eq "^${key}=.+" "${ENV_FILE}"; then
      echo "Missing required value: ${key}" >&2
      exit 1
    fi
  done

  docker compose --env-file "${ENV_FILE}" -f "${COMPOSE_FILE}" config --quiet
else
  for key in "${required[@]}"; do
    if [[ -z "${!key:-}" ]]; then
      echo "Missing required environment value: ${key}" >&2
      exit 1
    fi
  done

  if [[ "${API_DOMAIN}" == *.example.com || "${ORGANIZER_API_DOMAIN}" == *.example.com ]]; then
    echo "Production domains still use example.com placeholders." >&2
    exit 1
  fi

  docker compose -f "${COMPOSE_FILE}" config --quiet
fi
echo "Preflight checks passed."

