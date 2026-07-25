#!/usr/bin/env bash
# Run the bounded k6 concurrency smoke against the hermetic stack.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT}"

if ! command -v k6 >/dev/null 2>&1; then
  echo "k6 is required: https://grafana.com/docs/k6/latest/set-up/install-k6/" >&2
  exit 1
fi

if ! curl -fsS "${BFF_URL:-http://localhost:8080}/health" >/dev/null; then
  ./scripts/ci-start-hermetic-stack.sh
fi

k6 run \
  -e "BFF_URL=${BFF_URL:-http://localhost:8080}" \
  -e "VUS=${VUS:-10}" \
  load/hermetic-smoke.js
