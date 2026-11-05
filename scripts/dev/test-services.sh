#!/usr/bin/env bash
# Start throwaway ParadeDB and Redis for integration tests in a cloud or local dev container.
# Never point these variables at production services.
#
#   scripts/dev/test-services.sh up     # start dockerd if needed, then the containers
#   eval "$(scripts/dev/test-services.sh env)"
#   scripts/dev/test-services.sh down   # remove the containers and their data
set -euo pipefail

# Anonymous Docker Hub pulls from shared cloud IPs often hit 429; the Google mirror serves the same images.
REGISTRY="${TEST_SERVICES_REGISTRY:-mirror.gcr.io}"
PG_IMAGE="$REGISTRY/paradedb/paradedb:0.24.1-pg18"
REDIS_IMAGE="$REGISTRY/library/redis:7-alpine"
PG_PORT="${TEST_SERVICES_PG_PORT:-5432}"
REDIS_PORT="${TEST_SERVICES_REDIS_PORT:-6379}"
CONN="Host=localhost;Port=$PG_PORT;Database=equitylens_ci;Username=equitylens_ci;Password=ci_only_password"

ensure_docker() {
  docker info >/dev/null 2>&1 && return
  command -v dockerd >/dev/null || { echo "docker is not available" >&2; exit 1; }
  (dockerd >/tmp/dockerd.log 2>&1 &)
  for _ in $(seq 1 30); do docker info >/dev/null 2>&1 && return; sleep 1; done
  echo "dockerd did not start; see /tmp/dockerd.log" >&2
  exit 1
}

up() {
  ensure_docker
  docker inspect el-test-pg >/dev/null 2>&1 || docker run -d --name el-test-pg \
    -e POSTGRES_DB=equitylens_ci -e POSTGRES_USER=equitylens_ci -e POSTGRES_PASSWORD=ci_only_password \
    -p "$PG_PORT:5432" "$PG_IMAGE" >/dev/null
  docker inspect el-test-redis >/dev/null 2>&1 || docker run -d --name el-test-redis -p "$REDIS_PORT:6379" "$REDIS_IMAGE" >/dev/null
  docker start el-test-pg el-test-redis >/dev/null
  for _ in $(seq 1 60); do
    docker exec el-test-pg pg_isready -U equitylens_ci -d equitylens_ci >/dev/null 2>&1 && break
    sleep 2
  done
  docker exec el-test-pg pg_isready -U equitylens_ci -d equitylens_ci >/dev/null
  [ "$(docker exec el-test-redis redis-cli ping)" = PONG ]
  echo "ParadeDB on localhost:$PG_PORT, Redis on localhost:$REDIS_PORT"
}

env_vars() {
  cat <<EOF
export EQUITYLENS_TEST_POSTGRES_CONNECTION_STRING='$CONN'
export RISK_TEST_POSTGRES='$CONN'
export RISK_TEST_REDIS='localhost:$REDIS_PORT'
export RISK_TEST_REDIS_URL='redis://localhost:$REDIS_PORT/0'
EOF
}

case "${1:-}" in
  up) up ;;
  env) env_vars ;;
  down) docker rm -f el-test-pg el-test-redis >/dev/null 2>&1 || true ;;
  *) echo "usage: $0 up|env|down" >&2; exit 2 ;;
esac
