#!/usr/bin/env bash
# Print hermetic stack status and logs (for CI failure diagnostics).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT}"

export WORDGAMES_BUILD_CONTEXT="${WORDGAMES_BUILD_CONTEXT:-./wordgames}"
COMPOSE=(docker compose -f docker-compose.yml -f docker-compose.ci.yml)

"${COMPOSE[@]}" ps -a
"${COMPOSE[@]}" logs --no-color
