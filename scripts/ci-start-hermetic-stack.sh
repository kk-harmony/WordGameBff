#!/usr/bin/env bash
# Start the hermetic CI stack (mock OIDC + db + wordgames + BFF + embed).
# Host ports align with scripts/local-dev-ports.sh (same defaults as run-podman-local.sh).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT}"

if [[ -f "${ROOT}/.env" ]]; then
  set -a
  # shellcheck disable=SC1091
  source "${ROOT}/.env"
  set +a
fi

# shellcheck source=local-dev-ports.sh
source "${ROOT}/scripts/local-dev-ports.sh"

export WORDGAMES_BUILD_CONTEXT="${WORDGAMES_BUILD_CONTEXT:-./wordgames}"

resolve_compose() {
  if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
    COMPOSE=(docker compose -f docker-compose.yml -f docker-compose.ci.yml)
  elif podman compose version >/dev/null 2>&1; then
    COMPOSE=(podman compose -f docker-compose.yml -f docker-compose.ci.yml)
  elif command -v podman-compose >/dev/null 2>&1; then
    COMPOSE=(podman-compose -f docker-compose.yml -f docker-compose.ci.yml)
  else
    echo "docker compose or podman compose is required to start the hermetic stack." >&2
    exit 1
  fi
}

resolve_compose

wait_for_url() {
  local url="$1"
  local attempts="$2"
  local delay_seconds="$3"

  for ((attempt = 1; attempt <= attempts; attempt++)); do
    if curl -sf "${url}" >/dev/null; then
      return 0
    fi
    sleep "${delay_seconds}"
  done

  echo "Timed out waiting for ${url}" >&2
  return 1
}

"${COMPOSE[@]}" up -d mock-oauth2
wait_for_url "http://localhost:${MOCK_OAUTH2_PORT}/isalive" 30 2

"${COMPOSE[@]}" up --build -d wordgamebff embed db wordgames
wait_for_url "http://localhost:${WORDGAMEBFF_HTTP_PORT}/health" 60 3
wait_for_url "http://localhost:${EMBED_HTTP_PORT}/health" 20 3

SEED_FILE="${WORDGAMES_BUILD_CONTEXT}/scripts/seed-nepali-secretwords.sql"
if [[ ! -f "${SEED_FILE}" ]]; then
  echo "Missing secret-word seed file: ${SEED_FILE}" >&2
  exit 1
fi

SECRET_WORD_COUNT="$(
  "${COMPOSE[@]}" exec -T db \
    psql -U mainuser -d wordgame -tAc 'SELECT count(*) FROM wrdgm.secretword'
)"
if [[ "${SECRET_WORD_COUNT//[[:space:]]/}" == "0" ]]; then
  "${COMPOSE[@]}" exec -T db \
    psql -v ON_ERROR_STOP=1 -U mainuser -d wordgame < "${SEED_FILE}"
fi
