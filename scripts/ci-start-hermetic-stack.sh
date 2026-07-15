#!/usr/bin/env bash
# Start the hermetic CI stack (mock OIDC + db + wordgames + BFF + embed).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT}"

export WORDGAMES_BUILD_CONTEXT="${WORDGAMES_BUILD_CONTEXT:-./wordgames}"
COMPOSE=(docker compose -f docker-compose.yml -f docker-compose.ci.yml)

"${COMPOSE[@]}" up -d mock-oauth2
timeout 60 bash -c 'until curl -sf http://localhost:8090/isalive; do sleep 2; done'

"${COMPOSE[@]}" up --build -d wordgamebff embed db wordgames
timeout 180 bash -c 'until curl -sf http://localhost:8080/health; do sleep 3; done'
timeout 60 bash -c 'until curl -sf http://localhost:8082/health; do sleep 3; done'
