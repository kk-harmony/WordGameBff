#!/usr/bin/env bash
# WordGameBff local stack: Podman (wordgames + wordgamebff + demo) + host PostgreSQL.
# Redis: reuse localhost:REDIS_PORT if reachable; else deploy a 2-node Redis Cluster in Podman.
# Usage: ./scripts/run-podman-local.sh          # interactive menu
#        ./scripts/run-podman-local.sh up       # non-interactive
# CustomAuth: production https://customauth.fly.dev/ only (no mocks).

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
COMPOSE_FILE="${REPO_ROOT}/compose.podman.yaml"
ENV_FILE="${REPO_ROOT}/.env"
ENV_EXAMPLE="${REPO_ROOT}/.env.example"
INIT_DB_SQL="${REPO_ROOT}/docker/init-db.sql"
LOCAL_DIR="${REPO_ROOT}/local"
EMBED_SRC="${REPO_ROOT}/frontend/dist/embed/v1.0.0/embed.js"
EMBED_DST="${LOCAL_DIR}/embed.js"
FRONTEND_DIR="${REPO_ROOT}/frontend"
REDIS_MANAGED_FLAG="${LOCAL_DIR}/.managed-redis"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

info()  { echo -e "${GREEN}==>${NC} $*"; }
warn()  { echo -e "${YELLOW}warning:${NC} $*"; }
error() { echo -e "${RED}error:${NC} $*" >&2; }

require_cmd() {
  if ! command -v "$1" >/dev/null 2>&1; then
    error "'$1' not found. $2"
    exit 1
  fi
}

load_env() {
  if [[ -f "${ENV_FILE}" ]]; then
    set -a
    # shellcheck disable=SC1090
    source "${ENV_FILE}"
    set +a
  elif [[ -f "${ENV_EXAMPLE}" ]]; then
    warn ".env not found — copying from .env.example"
    cp "${ENV_EXAMPLE}" "${ENV_FILE}"
    warn "Edit ${ENV_FILE} with CustomAuth credentials, then re-run."
    exit 1
  else
    error ".env and .env.example not found in ${REPO_ROOT}"
    exit 1
  fi

  POSTGRES_HOST="${POSTGRES_HOST:-host.containers.internal}"
  POSTGRES_PORT="${POSTGRES_PORT:-5432}"
  POSTGRES_USER="${POSTGRES_USER:-mainuser}"
  POSTGRES_PASSWORD="${POSTGRES_PASSWORD:-${QUARKUS_DATASOURCE_PASSWORD:-}}"
  WORDGAMEBFF_HTTP_PORT="${WORDGAMEBFF_HTTP_PORT:-8180}"
  DEMO_HTTP_PORT="${DEMO_HTTP_PORT:-3100}"
  REDIS_HOST="${REDIS_HOST:-host.containers.internal}"
  REDIS_PORT="${REDIS_PORT:-6379}"
  REDIS_CLUSTER_PORT_1="${REDIS_CLUSTER_PORT_1:-6479}"
  REDIS_CLUSTER_PORT_2="${REDIS_CLUSTER_PORT_2:-6480}"
  WORDGAMES_BUILD_CONTEXT="${WORDGAMES_BUILD_CONTEXT:-../../learningProjects/wordgames}"

  if [[ -z "${CUSTOMAUTH__CLIENTID:-}" || -z "${CUSTOMAUTH__CLIENTSECRET:-}" ]]; then
    error "CUSTOMAUTH__CLIENTID and CUSTOMAUTH__CLIENTSECRET must be set in .env"
    error "Register M2M credentials at https://customauth.fly.dev/ (production CustomAuth only)."
    exit 1
  fi

  local auth_authority="${CUSTOMAUTH__AUTHORITY:-https://customauth.fly.dev/}"
  local oidc_url="${OIDC_AUTH_SERVER_URL:-https://customauth.fly.dev/}"
  if [[ "${auth_authority}" != *"customauth.fly.dev"* ]] || [[ "${oidc_url}" != *"customauth.fly.dev"* ]]; then
    error "CustomAuth must use production issuer https://customauth.fly.dev/ (no local mocks)."
    exit 1
  fi

  if [[ -z "${POSTGRES_PASSWORD}" ]]; then
    error "POSTGRES_PASSWORD (or QUARKUS_DATASOURCE_PASSWORD) must be set in .env"
    exit 1
  fi

  if ! (cd "${REPO_ROOT}" && cd "${WORDGAMES_BUILD_CONTEXT}" 2>/dev/null); then
    error "WORDGAMES_BUILD_CONTEXT path does not exist: ${WORDGAMES_BUILD_CONTEXT}"
    exit 1
  fi

  # Default standalone backplane target (overridden by ensure_redis when deploying a cluster).
  if [[ -z "${REALTIME__BACKPLANE__CONNECTIONSTRING:-}" ]]; then
    REALTIME__BACKPLANE__CONNECTIONSTRING="host=${REDIS_HOST};port=${REDIS_PORT}"
  fi

  export WORDGAMES_BUILD_CONTEXT="$(cd "${REPO_ROOT}" && cd "${WORDGAMES_BUILD_CONTEXT}" && pwd)"
  export POSTGRES_HOST POSTGRES_PORT POSTGRES_USER POSTGRES_PASSWORD
  export QUARKUS_DATASOURCE_PASSWORD="${POSTGRES_PASSWORD}"
  export WORDGAMEBFF_HTTP_PORT DEMO_HTTP_PORT
  export REDIS_HOST REDIS_PORT REDIS_CLUSTER_PORT_1 REDIS_CLUSTER_PORT_2
  export REALTIME__BACKPLANE__CONNECTIONSTRING
}

COMPOSE_CMD=()

resolve_compose_cmd() {
  if podman compose version >/dev/null 2>&1; then
    COMPOSE_CMD=(podman compose)
  elif command -v podman-compose >/dev/null 2>&1; then
    COMPOSE_CMD=(podman-compose)
  else
    error "Neither 'podman compose' nor 'podman-compose' is available."
    error "Install podman-compose: brew install podman-compose"
    exit 1
  fi
}

require_podman() {
  require_cmd podman "Install Podman: https://podman.io/getting-started/installation"
  resolve_compose_cmd
  ensure_podman_connection
}

podman_reachable() {
  podman info >/dev/null 2>&1
}

ensure_podman_connection() {
  if podman_reachable; then
    return 0
  fi

  warn "Cannot connect to Podman — the Linux VM may be stopped or the socket is stale."

  if podman machine list --format '{{.Name}}' 2>/dev/null | grep -q .; then
    local machine
    machine="$(podman machine list --format '{{.Name}}' | head -1)"
    info "Restarting Podman machine '${machine}'..."
    podman machine stop "${machine}" >/dev/null 2>&1 || true
    if ! podman machine start "${machine}"; then
      error "Failed to start Podman machine '${machine}'."
      error "Try manually: podman machine stop && podman machine start"
      exit 1
    fi
    sleep 2
  fi

  if podman_reachable; then
    info "Podman connection restored."
    return 0
  fi

  error "Still cannot connect to Podman."
  error "Try: podman machine stop && podman machine start"
  error "Or:  podman system connection list"
  exit 1
}

compose() {
  "${COMPOSE_CMD[@]}" -f "${COMPOSE_FILE}" --env-file "${ENV_FILE}" "$@"
}

pg_ready() {
  pg_isready -h localhost -p "${POSTGRES_PORT}" -U "${POSTGRES_USER}" >/dev/null 2>&1
}

require_postgres() {
  require_cmd psql "Install PostgreSQL client tools (psql, pg_isready)."
  require_cmd pg_isready "Install PostgreSQL client tools (psql, pg_isready)."

  if ! pg_ready; then
    error "Local PostgreSQL is not running on localhost:${POSTGRES_PORT}."
    error "Start PostgreSQL, then try again."
    exit 1
  fi
}

redis_cli_ping() {
  local host="$1"
  local port="$2"
  if command -v redis-cli >/dev/null 2>&1; then
    redis-cli -h "${host}" -p "${port}" ping 2>/dev/null | grep -q PONG
    return $?
  fi

  # Fallback: ephemeral redis-cli via Podman (reaches host Redis through host.containers.internal).
  local probe_host="${host}"
  if [[ "${host}" == "localhost" || "${host}" == "127.0.0.1" ]]; then
    probe_host="host.containers.internal"
  fi
  podman run --rm --quiet docker.io/library/redis:7 \
    redis-cli -h "${probe_host}" -p "${port}" ping 2>/dev/null | grep -q PONG
}

host_redis_ready() {
  redis_cli_ping "localhost" "${REDIS_PORT}"
}

compose_redis_ready() {
  local service="$1"
  compose exec -T "${service}" redis-cli ping 2>/dev/null | grep -q PONG
}

wait_for_compose_redis() {
  local service="$1"
  local timeout="${2:-60}"
  local elapsed=0
  while [[ "${elapsed}" -lt "${timeout}" ]]; do
    if compose_redis_ready "${service}"; then
      return 0
    fi
    sleep 2
    elapsed=$((elapsed + 2))
  done
  return 1
}

cluster_already_ok() {
  compose exec -T redis-1 redis-cli CLUSTER INFO 2>/dev/null | grep -q "cluster_state:ok"
}

init_managed_redis_cluster() {
  if cluster_already_ok; then
    info "Managed Redis cluster already initialized."
    return 0
  fi

  info "Initializing 2-node Redis Cluster (redis-1 + redis-2)..."
  local redis2_ip
  redis2_ip="$(compose exec -T redis-2 hostname -i 2>/dev/null | tr -d '\r' | awk '{print $1}')"
  if [[ -z "${redis2_ip}" ]]; then
    error "Could not resolve redis-2 container IP for CLUSTER MEET."
    exit 1
  fi

  compose exec -T redis-1 redis-cli CLUSTER MEET "${redis2_ip}" 6379 >/dev/null
  sleep 1

  # Split hash slots across the two masters (Redis Cluster create requires 3+ via redis-cli).
  compose exec -T redis-1 sh -c 'seq 0 8191 | xargs -n 100 redis-cli CLUSTER ADDSLOTS' >/dev/null
  compose exec -T redis-2 sh -c 'seq 8192 16383 | xargs -n 100 redis-cli CLUSTER ADDSLOTS' >/dev/null

  local timeout=30
  local elapsed=0
  while [[ "${elapsed}" -lt "${timeout}" ]]; do
    if cluster_already_ok; then
      info "Redis cluster is ready (cluster_state:ok)."
      return 0
    fi
    sleep 1
    elapsed=$((elapsed + 1))
  done

  error "Managed Redis cluster did not reach cluster_state:ok within ${timeout}s."
  compose logs redis-1 redis-2 || true
  exit 1
}

start_managed_redis_cluster() {
  info "No Redis on localhost:${REDIS_PORT} — deploying 2-node Redis Cluster via Podman..."
  if ! compose --profile managed-redis up -d redis-1 redis-2; then
    warn "Compose --profile failed; starting redis-1/redis-2 by name..."
    compose up -d redis-1 redis-2
  fi

  if ! wait_for_compose_redis redis-1 || ! wait_for_compose_redis redis-2; then
    error "Managed Redis nodes did not become healthy."
    compose logs redis-1 redis-2 || true
    exit 1
  fi

  init_managed_redis_cluster

  REALTIME__BACKPLANE__CONNECTIONSTRING="cluster://redis-1:6379,redis-2:6379"
  export REALTIME__BACKPLANE__CONNECTIONSTRING
  mkdir -p "${LOCAL_DIR}"
  echo "managed" > "${REDIS_MANAGED_FLAG}"
  info "Using managed Redis cluster: ${REALTIME__BACKPLANE__CONNECTIONSTRING}"
  info "Host debug ports: localhost:${REDIS_CLUSTER_PORT_1}, localhost:${REDIS_CLUSTER_PORT_2}"
}

ensure_redis() {
  if host_redis_ready; then
    REALTIME__BACKPLANE__CONNECTIONSTRING="host=${REDIS_HOST};port=${REDIS_PORT}"
    export REALTIME__BACKPLANE__CONNECTIONSTRING
    rm -f "${REDIS_MANAGED_FLAG}"
    info "Using existing Redis on localhost:${REDIS_PORT} (${REALTIME__BACKPLANE__CONNECTIONSTRING})"
    return 0
  fi

  # Prefer an already-running managed cluster from a previous up.
  if compose_redis_ready redis-1 2>/dev/null && compose_redis_ready redis-2 2>/dev/null; then
    init_managed_redis_cluster
    REALTIME__BACKPLANE__CONNECTIONSTRING="cluster://redis-1:6379,redis-2:6379"
    export REALTIME__BACKPLANE__CONNECTIONSTRING
    mkdir -p "${LOCAL_DIR}"
    echo "managed" > "${REDIS_MANAGED_FLAG}"
    info "Reusing managed Redis cluster: ${REALTIME__BACKPLANE__CONNECTIONSTRING}"
    return 0
  fi

  start_managed_redis_cluster
}

redis_status_line() {
  if host_redis_ready; then
    info "Redis: existing host instance (localhost:${REDIS_PORT})"
  elif [[ -f "${REDIS_MANAGED_FLAG}" ]] || compose_redis_ready redis-1 2>/dev/null; then
    if cluster_already_ok 2>/dev/null; then
      info "Redis: managed 2-node cluster (redis-1/redis-2, host :${REDIS_CLUSTER_PORT_1}/:${REDIS_CLUSTER_PORT_2})"
    else
      warn "Redis: managed containers present but cluster not ok"
    fi
  else
    warn "Redis: not reachable"
  fi
}

database_exists() {
  local db_name="$1"
  local result
  result="$(PGPASSWORD="${POSTGRES_PASSWORD}" psql -h localhost -p "${POSTGRES_PORT}" -U "${POSTGRES_USER}" -d postgres -tAc \
    "SELECT 1 FROM pg_database WHERE datname='${db_name}'" 2>/dev/null || true)"
  [[ "${result}" == "1" ]]
}

cmd_init_db() {
  require_postgres
  info "Initializing databases on host PostgreSQL..."

  local created=0
  for db in wordgame wordgamebff; do
    if database_exists "${db}"; then
      info "Database '${db}' already exists — skipping"
    else
      info "Creating database '${db}'..."
      PGPASSWORD="${POSTGRES_PASSWORD}" psql -h localhost -p "${POSTGRES_PORT}" -U "${POSTGRES_USER}" -d postgres \
        -c "CREATE DATABASE ${db};"
      created=1
    fi
  done

  if [[ -f "${INIT_DB_SQL}" ]]; then
    info "Applying grants from docker/init-db.sql..."
    PGPASSWORD="${POSTGRES_PASSWORD}" psql -h localhost -p "${POSTGRES_PORT}" -U "${POSTGRES_USER}" -d postgres \
      -v ON_ERROR_STOP=0 -f "${INIT_DB_SQL}" 2>/dev/null || true
  fi

  if [[ "${created}" -eq 1 ]]; then
    info "Databases created."
  else
    info "All databases already present."
  fi
}

ensure_databases() {
  if ! database_exists "wordgame" || ! database_exists "wordgamebff"; then
    warn "Databases missing — running init-db..."
    cmd_init_db
  fi
}

ensure_embed() {
  mkdir -p "${LOCAL_DIR}"

  if [[ ! -f "${EMBED_SRC}" ]]; then
    info "Building micro frontend embed (frontend/)..."
    require_cmd npm "Install Node.js/npm to build the embed bundle."
    (cd "${FRONTEND_DIR}" && npm run build)
  fi

  if [[ ! -f "${EMBED_SRC}" ]]; then
    error "Embed bundle not found at ${EMBED_SRC}"
    error "Run: cd frontend && npm run build"
    exit 1
  fi

  cp "${EMBED_SRC}" "${EMBED_DST}"
  info "Copied embed.js to local/embed.js"
}

wait_for_wordgamebff() {
  local url="http://localhost:${WORDGAMEBFF_HTTP_PORT}/health"
  local timeout=120
  local elapsed=0
  info "Waiting for WordGameBff at ${url}..."
  while [[ "${elapsed}" -lt "${timeout}" ]]; do
    if curl -sf "${url}" >/dev/null 2>&1; then
      info "WordGameBff is healthy."
      return 0
    fi
    sleep 2
    elapsed=$((elapsed + 2))
  done
  error "WordGameBff did not become healthy within ${timeout}s"
  compose logs wordgamebff
  exit 1
}

cmd_build() {
  local no_cache="${1:-}"
  if [[ "${no_cache}" == "--no-cache" ]]; then
    info "Rebuilding wordgames + wordgamebff (no cache)..."
    compose build --no-cache wordgames wordgamebff
  else
    info "Building wordgames + wordgamebff..."
    compose build wordgames wordgamebff
  fi
}

cmd_up() {
  require_postgres
  ensure_databases
  ensure_redis
  ensure_embed
  info "Building and starting wordgames + wordgamebff + demo..."
  compose up --build -d wordgames wordgamebff demo
  wait_for_wordgamebff
  print_stack_urls
}

print_stack_urls() {
  echo ""
  info "Stack is up."
  echo "  Demo:        http://localhost:${DEMO_HTTP_PORT}  (HTML + micro frontend)"
  echo "  WordGameBff: http://localhost:${WORDGAMEBFF_HTTP_PORT}/health"
  echo "  wordgames: internal only (curl http://localhost:8081 should fail)"
  echo "  Redis:       ${REALTIME__BACKPLANE__CONNECTIONSTRING}"
  echo ""
  echo "CustomAuth: ${CUSTOMAUTH__AUTHORITY:-https://customauth.fly.dev/}"
}

cmd_rebuild() {
  cmd_down || true
  require_postgres
  ensure_databases
  ensure_redis
  ensure_embed
  cmd_build --no-cache
  compose up -d wordgames wordgamebff demo
  wait_for_wordgamebff
  print_stack_urls
}

cmd_restart() {
  cmd_rebuild
}

cmd_down() {
  info "Stopping containers..."
  # Include managed-redis profile so redis-1/redis-2 are removed when we started them.
  if ! compose --profile managed-redis down; then
    error "Failed to stop containers. If Podman was disconnected, run: ./scripts/run-podman-local.sh restart"
    exit 1
  fi
  rm -f "${REDIS_MANAGED_FLAG}"
  info "Containers stopped. Host PostgreSQL / host Redis (if any) are still running."
}

cmd_status() {
  compose --profile managed-redis ps
  echo ""
  if pg_ready; then
    info "Host PostgreSQL: running (localhost:${POSTGRES_PORT})"
  else
    warn "Host PostgreSQL: not reachable"
  fi
  redis_status_line
  if curl -sf "http://localhost:${WORDGAMEBFF_HTTP_PORT}/health" >/dev/null 2>&1; then
    info "WordGameBff health: OK"
  else
    warn "WordGameBff health: not responding"
  fi
  if curl -sf "http://localhost:${DEMO_HTTP_PORT}/health" >/dev/null 2>&1; then
    info "Demo page: OK (http://localhost:${DEMO_HTTP_PORT})"
  else
    warn "Demo page: not responding"
  fi
}

cmd_logs() {
  local service="${1:-}"
  if [[ -n "${service}" ]]; then
    compose logs -f "${service}"
  else
    compose logs -f
  fi
}

cmd_health() {
  info "Checking WordGameBff..."
  if curl -sf "http://localhost:${WORDGAMEBFF_HTTP_PORT}/health"; then
    echo ""
    info "WordGameBff: OK"
  else
    error "WordGameBff: not healthy"
    exit 1
  fi

  info "Checking demo page..."
  if curl -sf "http://localhost:${DEMO_HTTP_PORT}/health" >/dev/null 2>&1; then
    info "Demo: OK"
  else
    warn "Demo: not responding"
  fi

  redis_status_line

  info "Checking wordgames is NOT exposed on host :8081..."
  if curl -sf --connect-timeout 2 "http://localhost:8081/q/health/ready" >/dev/null 2>&1; then
    warn "wordgames responded on localhost:8081 (expected internal-only)"
  else
    info "wordgames: correctly not exposed on host port 8081"
  fi
}

run_command() {
  local cmd="${1:-}"
  shift || true

  case "${cmd}" in
    up)       cmd_up ;;
    down)     cmd_down ;;
    restart)  cmd_restart ;;
    rebuild)  cmd_rebuild ;;
    init-db)  cmd_init_db ;;
    status)   cmd_status ;;
    logs)     cmd_logs "${1:-}" ;;
    health)   cmd_health ;;
    "")
      error "No command specified."
      exit 1
      ;;
    *)
      error "Unknown command: ${cmd}"
      error "Valid: up, down, restart, rebuild, init-db, status, logs, health"
      exit 1
      ;;
  esac
}

show_menu() {
  echo ""
  echo "WordGameBff local stack (Podman + host PostgreSQL)"
  echo ""
  echo "  1) Up        — preflight Postgres/Redis, build + start wordgames + wordgamebff + demo"
  echo "  2) Down      — stop and remove containers (incl. managed Redis cluster if started)"
  echo "  3) Restart   — down then up"
  echo "  4) Init DB   — create wordgame + wordgamebff on host Postgres"
  echo "  5) Status    — container status + health summary"
  echo "  6) Logs      — follow all service logs"
  echo "  7) Logs      — wordgamebff only"
  echo "  8) Logs      — wordgames only"
  echo "  9) Logs      — demo only"
  echo " 10) Health    — curl wordgamebff; confirm wordgames NOT on host 8081"
  echo "  0) Exit"
  echo ""
  echo "  Demo URL when up: http://localhost:${DEMO_HTTP_PORT:-3100}"
  echo "  Redis: reuse localhost:${REDIS_PORT:-6379} if up, else Podman 2-node cluster"
  echo ""
}

interactive_loop() {
  while true; do
    show_menu
    read -r -p "Choose [0-10]: " choice
    case "${choice}" in
      1) cmd_up ;;
      2) cmd_down ;;
      3) cmd_restart ;;
      4) cmd_init_db ;;
      5) cmd_status ;;
      6) cmd_logs ;;
      7) cmd_logs wordgamebff ;;
      8) cmd_logs wordgames ;;
      9) cmd_logs demo ;;
      10) cmd_health ;;
      0) info "Bye."; exit 0 ;;
      *) warn "Invalid choice. Enter 0-10." ;;
    esac
    echo ""
    read -r -p "Press Enter to continue..."
  done
}

main() {
  cd "${REPO_ROOT}"
  require_podman
  load_env

  if [[ $# -eq 0 ]]; then
    interactive_loop
  else
    run_command "$@"
  fi
}

main "$@"
