# TicketShield production deployment

This directory deploys the complete TicketShield backend on one Ubuntu VPS:

- Caddy terminates HTTPS on ports 80/443.
- `gateway` exposes Identity/Core through `API_DOMAIN`.
- `anti-bot-gateway` exposes the protected organizer API through `ORGANIZER_API_DOMAIN`.
- PostgreSQL, Redis, RabbitMQ, Identity, Core, Settlement and Mock Organizer stay on an internal Docker network and do not publish host ports.

## 1. VPS prerequisites

Use Ubuntu 24.04 LTS. Install Docker Engine and the Docker Compose plugin from Docker's official Ubuntu repository:

https://docs.docker.com/engine/install/ubuntu/

Only allow inbound SSH, HTTP and HTTPS at the provider firewall:

```text
22/tcp   restricted to trusted IPs when possible
80/tcp   public
443/tcp  public
443/udp  public (HTTP/3; optional)
```

Do not publish PostgreSQL 5432, Redis 6379, RabbitMQ 5672/15672 or application ports 5000-5006.

## 2. DNS

Before starting Caddy, create these DNS `A` records pointing to the VPS public IPv4:

```text
api.example.com            -> VPS_IP
organizer-api.example.com  -> VPS_IP
```

If Cloudflare proxying is enabled, use SSL/TLS mode `Full (strict)`. Start with DNS-only records if certificate issuance needs troubleshooting.

## 3. Repository and environment

Clone the backend and enter it:

```bash
git clone <BACKEND_GIT_URL> TicketShield
cd TicketShield
cp deploy/.env.example deploy/.env
chmod 600 deploy/.env
```

Edit `deploy/.env`. Generate a different random value for every required secret:

```bash
openssl rand -hex 32
```

Never commit `deploy/.env`.

### Doppler option

Create a production config such as `ticketshield_key/prd` containing the variable names from `deploy/.env.example`. Then either inject them without a file:

```bash
doppler run -p ticketshield_key -c prd -- \
  docker compose -f deploy/compose.production.yaml config --quiet

doppler run -p ticketshield_key -c prd -- \
  docker compose -f deploy/compose.production.yaml up -d --build
```

Or materialize a root-readable file when a cron backup job needs it:

```bash
doppler secrets download -p ticketshield_key -c prd --no-file --format env > deploy/.env
chmod 600 deploy/.env
```

The file form is convenient but stores secrets on disk. Prefer `doppler run` when possible.

## 4. Validate and start

With `deploy/.env`:

```bash
bash deploy/scripts/preflight.sh
docker compose --env-file deploy/.env -f deploy/compose.production.yaml config --quiet
docker compose --env-file deploy/.env -f deploy/compose.production.yaml build
docker compose --env-file deploy/.env -f deploy/compose.production.yaml up -d
docker compose --env-file deploy/.env -f deploy/compose.production.yaml ps
```

Watch first-start migrations and certificate issuance:

```bash
docker compose --env-file deploy/.env -f deploy/compose.production.yaml logs -f --tail=200
```

Verify public endpoints:

```bash
curl -fsS "https://${API_DOMAIN}/"
curl -fsS "https://${ORGANIZER_API_DOMAIN}/health"
```

The initial database startup creates `ticketshield_db`, `identity_db`, `organizer_db`, and `settlement_db`. Each service then applies its own schema initialization/migrations.

## 5. Connect the frontends

Set and redeploy the Vercel project:

```text
VITE_API_BASE_URL=https://api.example.com/api/v1
VITE_ORGANIZER_API_BASE_URL=https://organizer-api.example.com
```

Use the corresponding `EXPO_PUBLIC_*` variables when building the mobile app.

`CORS_ALLOWED_ORIGINS` must contain the exact browser origins, comma-separated and without trailing slashes. Example:

```text
https://fe-capstone-project-web.vercel.app,https://ticketshield.vn,https://www.ticketshield.vn
```

## 6. Backups

Create a manual compressed backup:

```bash
bash deploy/scripts/backup-postgres.sh
```

The script keeps seven days by default. Copy backups off the VPS; a backup on the same disk does not protect against disk/VPS loss.

Example daily cron entry at 02:15 UTC:

```cron
15 2 * * * cd /opt/ticketshield/TicketShield && /usr/bin/bash deploy/scripts/backup-postgres.sh >> /var/log/ticketshield-backup.log 2>&1
```

Adjust the repository path to the actual clone location.

## 7. Deploy updates

```bash
git pull --ff-only
docker compose --env-file deploy/.env -f deploy/compose.production.yaml build
docker compose --env-file deploy/.env -f deploy/compose.production.yaml up -d
docker compose --env-file deploy/.env -f deploy/compose.production.yaml ps
```

Do not run `docker compose down -v`; `-v` deletes database and queue volumes.

## Security checklist

- Use SSH keys and disable password-based SSH after confirming key access.
- Keep `deploy/.env` mode `600`, or inject through Doppler.
- Rotate any credential that has ever been committed to Git; overriding it at runtime does not remove it from Git history.
- Keep database, Redis, RabbitMQ and internal API ports unpublished.
- Store PostgreSQL backups off the VPS and test restoration periodically.
- Update Ubuntu and Docker regularly, then verify the stack after updates.

