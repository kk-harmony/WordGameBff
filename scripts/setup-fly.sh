#!/usr/bin/env bash
# One-time Fly bootstrap for WordGameBff: app + Postgres + credential secrets.
# Non-secrets (URLs, CORS, Authority) live in fly.toml [env] — edit there, not here.
#
# Usage:
#   cp .env.fly.example .env.fly   # fill secrets
#   ./scripts/setup-fly.sh
#
# After this: add GitHub secrets, then Actions (or fly deploy) can ship the app.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
ENV_FILE="${REPO_ROOT}/.env.fly"
APP_NAME="${FLY_APP_NAME:-wordgamebff}"
PG_NAME="${FLY_PG_NAME:-wordgamebff-db}"
REGION="${FLY_REGION:-iad}"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

info()  { echo -e "${GREEN}==>${NC} $*"; }
warn()  { echo -e "${YELLOW}warning:${NC} $*"; }
error() { echo -e "${RED}error:${NC} $*" >&2; }

require_cmd() {
  if ! command -v "$1" >/dev/null 2>&1; then
    error "Required command not found: $1"
    exit 1
  fi
}

load_env_file() {
  if [[ ! -f "$ENV_FILE" ]]; then
    error "Missing $ENV_FILE — copy .env.fly.example to .env.fly and fill secrets."
    exit 1
  fi
  set -a
  # shellcheck disable=SC1090
  source "$ENV_FILE"
  set +a
}

app_exists() {
  fly status -a "$APP_NAME" >/dev/null 2>&1
}

pg_exists() {
  fly status -a "$PG_NAME" >/dev/null 2>&1
}

resolve_npgsql() {
  local cs="${REALTIME__BACKPLANE__CONNECTIONSTRING:-}"
  if [[ -n "$cs" && "$cs" != *"YOUR_HOST"* && "$cs" != *"YOUR_PASSWORD"* ]]; then
    if [[ "$cs" != *"SSL Mode"* && "$cs" != *"Ssl Mode"* ]]; then
      cs="${cs};SSL Mode=Require"
    fi
    printf '%s' "$cs"
    return
  fi

  if [[ -z "${FLY_PG_HOST:-}" || -z "${FLY_PG_DB:-}" || -z "${FLY_PG_USER:-}" || -z "${FLY_PG_PASSWORD:-}" ]]; then
    error "Set REALTIME__BACKPLANE__CONNECTIONSTRING (Npgsql) or FLY_PG_HOST/DB/USER/PASSWORD in .env.fly"
    error "Get values via: fly postgres db list -a ${PG_NAME}  (or Fly Postgres dashboard)"
    error "Do NOT use Fly attach DATABASE_URL (postgres:// URI) — same as CustomAuth."
    exit 1
  fi

  printf 'Host=%s;Port=%s;Database=%s;Username=%s;Password=%s;SSL Mode=Require' \
    "$FLY_PG_HOST" \
    "${FLY_PG_PORT:-5432}" \
    "$FLY_PG_DB" \
    "$FLY_PG_USER" \
    "$FLY_PG_PASSWORD"
}

main() {
  require_cmd fly
  require_cmd openssl
  load_env_file

  if [[ -z "${CUSTOMAUTH__CLIENTID:-}" || -z "${CUSTOMAUTH__CLIENTSECRET:-}" ]]; then
    error "CUSTOMAUTH__CLIENTID and CUSTOMAUTH__CLIENTSECRET are required in .env.fly"
    exit 1
  fi

  local npgsql
  npgsql="$(resolve_npgsql)"

  local session_key="${SESSION__SIGNINGKEY:-}"
  if [[ -z "$session_key" || "$session_key" == change-me* ]]; then
    session_key="$(openssl rand -base64 48)"
    info "Generated SESSION__SIGNINGKEY"
  fi

  if ! fly auth whoami >/dev/null 2>&1; then
    error "Not logged in to Fly. Run: fly auth login"
    exit 1
  fi

  if app_exists; then
    info "App ${APP_NAME} already exists"
  else
    info "Creating app ${APP_NAME}"
    fly apps create "$APP_NAME"
  fi

  if pg_exists; then
    info "Postgres ${PG_NAME} already exists"
  else
    info "Creating Postgres ${PG_NAME} in ${REGION}"
    fly postgres create --name "$PG_NAME" --region "$REGION"
  fi

  info "Attaching Postgres ${PG_NAME} to ${APP_NAME} (may set unused DATABASE_URL — ignore it)"
  if ! fly postgres attach "$PG_NAME" --app "$APP_NAME" 2>/dev/null; then
    warn "Attach skipped or already attached — continuing"
  fi

  info "Setting Fly secrets (credentials only; URLs/CORS are in fly.toml)"
  fly secrets set -a "$APP_NAME" \
    "REALTIME__BACKPLANE__CONNECTIONSTRING=${npgsql}" \
    "SESSION__SIGNINGKEY=${session_key}" \
    "CUSTOMAUTH__CLIENTID=${CUSTOMAUTH__CLIENTID}" \
    "CUSTOMAUTH__CLIENTSECRET=${CUSTOMAUTH__CLIENTSECRET}"

  echo
  info "Bootstrap complete."
  echo
  echo "Next steps:"
  echo "  1. Edit fly.toml [env] CORS / GameApi__BaseUrl if still placeholders, then commit."
  echo "  2. Create a deploy token and add GitHub Actions secrets:"
  echo "       fly tokens create deploy -a ${APP_NAME} -x 999999h"
  echo "       → repository secret FLY_API_TOKEN"
  echo "       → NETLIFY_AUTH_TOKEN and NETLIFY_SITE_ID for frontend deploy"
  echo "  3. Push to main or run workflow_dispatch on Deploy BFF / Deploy frontend."
  echo
  echo "Verify after first deploy:"
  echo "  curl -sS https://${APP_NAME}.fly.dev/health/live"
  echo "  curl -sS https://${APP_NAME}.fly.dev/health/ready"
}

main "$@"
