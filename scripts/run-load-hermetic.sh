#!/usr/bin/env bash
# Run the bounded k6 concurrency smoke against the local/hermetic BFF.
# Ports align with scripts/local-dev-ports.sh (same defaults as run-podman-local.sh).
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

if ! command -v k6 >/dev/null 2>&1; then
  echo "k6 is required: https://grafana.com/docs/k6/latest/set-up/install-k6/" >&2
  exit 1
fi

if ! curl -fsS "${BFF_URL}/health" >/dev/null; then
  ./scripts/ci-start-hermetic-stack.sh
fi

k6 run \
  -e "BFF_URL=${BFF_URL}" \
  -e "VUS=${VUS:-10}" \
  load/hermetic-smoke.js
