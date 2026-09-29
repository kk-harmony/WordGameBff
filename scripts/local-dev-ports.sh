# Shared local host-port defaults for Podman, hermetic compose, and load smoke.
# Offset from common defaults (8080/3000/5173/8082) to avoid clashes with other stacks.
# Source after optional .env so file values win when set; otherwise these defaults apply.
#
# Usage: # shellcheck source=local-dev-ports.sh
#        source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/local-dev-ports.sh"

: "${WORDGAMEBFF_HTTP_PORT:=8180}"
: "${DEMO_HTTP_PORT:=3100}"
: "${EMBED_HTTP_PORT:=8083}"
: "${PLAYGROUND_HTTP_PORT:=5174}"
: "${MOCK_OAUTH2_PORT:=8090}"
: "${POSTGRES_PORT:=5432}"
: "${REDIS_PORT:=6379}"
: "${REDIS_CLUSTER_PORT_1:=6479}"
: "${REDIS_CLUSTER_PORT_2:=6480}"

# Keep localhost BFF_URL aligned with WORDGAMEBFF_HTTP_PORT (explicit remote URLs are left alone).
if [[ -z "${BFF_URL:-}" || "${BFF_URL}" == http://localhost:* || "${BFF_URL}" == http://127.0.0.1:* ]]; then
  BFF_URL="http://localhost:${WORDGAMEBFF_HTTP_PORT}"
fi

export WORDGAMEBFF_HTTP_PORT DEMO_HTTP_PORT EMBED_HTTP_PORT PLAYGROUND_HTTP_PORT
export MOCK_OAUTH2_PORT POSTGRES_PORT REDIS_PORT REDIS_CLUSTER_PORT_1 REDIS_CLUSTER_PORT_2
export BFF_URL
