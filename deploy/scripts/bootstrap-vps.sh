#!/usr/bin/env bash
set -Eeuo pipefail

trap 'echo "Bootstrap failed at line ${LINENO}." >&2' ERR

if [[ "${EUID}" -ne 0 ]]; then
  echo "Run this bootstrap script as root." >&2
  exit 1
fi

APP_USER="${APP_USER:-ticketshield-deploy}"
APP_ROOT="${APP_ROOT:-/opt/ticketshield}"
REPO_DIR="${REPO_DIR:-${APP_ROOT}/repo}"
REPO_URL="${REPO_URL:-https://github.com/Thong-LH/TicketShield.git}"
REPO_BRANCH="${REPO_BRANCH:-deploy/production-vps}"
DOPPLER_PROJECT="${DOPPLER_PROJECT:-ticketshield_key}"
DOPPLER_CONFIG="${DOPPLER_CONFIG:-prd}"
CONFIG_DIR="/etc/ticketshield"
TOKEN_FILE="${CONFIG_DIR}/doppler-token"

if [[ ! -r /etc/os-release ]]; then
  echo "Unable to identify the operating system." >&2
  exit 1
fi

# shellcheck disable=SC1091
source /etc/os-release
if [[ "${ID:-}" != "ubuntu" ]]; then
  echo "This script supports Ubuntu only; detected ${ID:-unknown}." >&2
  exit 1
fi

export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y ca-certificates curl git gnupg openssl sudo ufw util-linux

if ! command -v docker >/dev/null 2>&1; then
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
  chmod a+r /etc/apt/keyrings/docker.asc

  cat >/etc/apt/sources.list.d/docker.sources <<EOF
Types: deb
URIs: https://download.docker.com/linux/ubuntu
Suites: ${UBUNTU_CODENAME:-${VERSION_CODENAME}}
Components: stable
Architectures: $(dpkg --print-architecture)
Signed-By: /etc/apt/keyrings/docker.asc
EOF

  apt-get update
  apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
fi

systemctl enable --now docker

if ! command -v doppler >/dev/null 2>&1; then
  curl -sLf --retry 3 --tlsv1.2 --proto '=https' \
    https://packages.doppler.com/public/cli/gpg.DE2A7741A397C129.key \
    | gpg --dearmor --yes -o /usr/share/keyrings/doppler-archive-keyring.gpg
  echo "deb [signed-by=/usr/share/keyrings/doppler-archive-keyring.gpg] https://packages.doppler.com/public/cli/deb/debian any-version main" \
    >/etc/apt/sources.list.d/doppler-cli.list
  apt-get update
  apt-get install -y doppler
fi

if ! id "${APP_USER}" >/dev/null 2>&1; then
  useradd --create-home --shell /bin/bash "${APP_USER}"
fi
usermod -aG docker "${APP_USER}"

install -d -m 0750 -o "${APP_USER}" -g "${APP_USER}" "${APP_ROOT}"
install -d -m 0750 -o root -g "${APP_USER}" "${CONFIG_DIR}"

# Reuse the root SSH key for the deployment user when key authentication has
# already been configured. Password authentication settings are not changed.
if [[ -s /root/.ssh/authorized_keys ]]; then
  install -d -m 0700 -o "${APP_USER}" -g "${APP_USER}" "/home/${APP_USER}/.ssh"
  install -m 0600 -o "${APP_USER}" -g "${APP_USER}" \
    /root/.ssh/authorized_keys "/home/${APP_USER}/.ssh/authorized_keys"
fi

ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw allow 443/udp
ufw --force enable

if [[ ! -d "${REPO_DIR}/.git" ]]; then
  sudo -u "${APP_USER}" git clone --branch "${REPO_BRANCH}" --single-branch \
    "${REPO_URL}" "${REPO_DIR}"
else
  sudo -u "${APP_USER}" git -C "${REPO_DIR}" remote set-url origin "${REPO_URL}"
fi

cat >"${CONFIG_DIR}/deploy.conf" <<EOF
APP_USER='${APP_USER}'
APP_ROOT='${APP_ROOT}'
REPO_DIR='${REPO_DIR}'
REPO_URL='${REPO_URL}'
REPO_BRANCH='${REPO_BRANCH}'
DOPPLER_PROJECT='${DOPPLER_PROJECT}'
DOPPLER_CONFIG='${DOPPLER_CONFIG}'
DOPPLER_TOKEN_FILE='${TOKEN_FILE}'
EOF
chmod 0640 "${CONFIG_DIR}/deploy.conf"
chown root:"${APP_USER}" "${CONFIG_DIR}/deploy.conf"

if [[ ! -s "${TOKEN_FILE}" ]]; then
  if [[ -n "${DOPPLER_TOKEN:-}" ]]; then
    token="${DOPPLER_TOKEN}"
  else
    read -r -s -p "Doppler service token for ${DOPPLER_PROJECT}/${DOPPLER_CONFIG}: " token
    echo
  fi

  if [[ -z "${token:-}" ]]; then
    echo "A Doppler service token is required." >&2
    exit 1
  fi

  printf '%s' "${token}" >"${TOKEN_FILE}"
  unset token
fi

chmod 0640 "${TOKEN_FILE}"
chown root:"${APP_USER}" "${TOKEN_FILE}"

sudo -u "${APP_USER}" -H env \
  DOPPLER_TOKEN="$(<"${TOKEN_FILE}")" \
  doppler secrets -p "${DOPPLER_PROJECT}" -c "${DOPPLER_CONFIG}" --only-names >/dev/null

sudo -u "${APP_USER}" -H bash "${REPO_DIR}/deploy/scripts/deploy-vps.sh"

echo "Bootstrap and deployment completed."

