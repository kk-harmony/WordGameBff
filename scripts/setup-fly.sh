#!/usr/bin/env bash
# One-time Fly bootstrap for WordGameBff: app + Redis + credential secrets.
# Shared BFF state and SignalR backplane both use Redis (Stores__Type=Redis in fly.toml).
# Non-secrets (URLs, CORS, Authority, BackplaneType) live in fly.toml [env] — edit there, not here.
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

resolve_redis() {
  local cs="${REALTIME__BACKPLANE__CONNECTIONSTRING:-}"
  if [[ -z "$cs" || "$cs" == *"YOUR_REDIS"* || "$cs" == *"YOUR_PASSWORD"* ]]; then
    error "Set REALTIME__BACKPLANE__CONNECTIONSTRING (Equinoctial Redis form) in .env.fly"
    error "Example: host=YOUR_REDIS_HOST;port=6379;username=default;password=YOUR_PASSWORD"
    error "Provision with: fly redis create   (Upstash) or any Redis reachable from the app"
    exit 1
  fi
  printf '%s' "$cs"
}

main() {
  require_cmd fly
  require_cmd openssl
  load_env_file

  if [[ -z "${CUSTOMAUTH__CLIENTID:-}" || -z "${CUSTOMAUTH__CLIENTSECRET:-}" ]]; then
    error "CUSTOMAUTH__CLIENTID and CUSTOMAUTH__CLIENTSECRET are required in .env.fly"
    exit 1
  fi

  local redis_cs
  redis_cs="$(resolve_redis)"

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

  info "Setting Fly secrets (credentials only; URLs/CORS/BackplaneType/Stores__Type are in fly.toml)"
  fly secrets set -a "$APP_NAME" \
    "REALTIME__BACKPLANE__CONNECTIONSTRING=${redis_cs}" \
    "SESSION__SIGNINGKEY=${session_key}" \
    "CUSTOMAUTH__CLIENTID=${CUSTOMAUTH__CLIENTID}" \
    "CUSTOMAUTH__CLIENTSECRET=${CUSTOMAUTH__CLIENTSECRET}"

  echo
  info "Bootstrap complete."
  echo
  echo "Next steps:"
  echo "  1. Ensure Upstash Redis exists (fly redis create / fly redis list) and the secret matches."
  echo "  2. Edit fly.toml [env] CORS / GameApi__BaseUrl if still placeholders, then commit."
  echo "  3. Create a deploy token and add GitHub Actions secrets:"
  echo "       fly tokens create deploy -a ${APP_NAME} -x 999999h"
  echo "       → repository secret FLY_API_TOKEN"
  echo "       → NETLIFY_AUTH_TOKEN and NETLIFY_SITE_ID for frontend deploy"
  echo "  4. Push to main or run workflow_dispatch on Deploy BFF / Deploy frontend."
  echo
  echo "Verify after first deploy:"
  echo "  curl -sS https://${APP_NAME}.fly.dev/health/live"
  echo "  curl -sS https://${APP_NAME}.fly.dev/health/ready"
}

main "$@"
