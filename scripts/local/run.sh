#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

API_URL="${API_URL:-http://localhost:5000}"
BILLING_URL="${BILLING_URL:-http://localhost:3001}"
API_LOG_PATH="${API_LOG_PATH:-$REPO_ROOT/.local-api.log}"
BILLING_LOG_PATH="${BILLING_LOG_PATH:-$REPO_ROOT/.local-billing.log}"
POSTGRES_HOST="${POSTGRES_HOST:-localhost}"
POSTGRES_PORT="${POSTGRES_PORT:-5432}"
REDIS_HOST="${REDIS_HOST:-localhost}"
REDIS_PORT="${REDIS_PORT:-6379}"

port_open() {
  (exec 3<>"/dev/tcp/$1/$2") 2>/dev/null
}

if [[ "${SKIP_DEPENDENCY_CHECK:-0}" != "1" ]]; then
  missing=0
  if ! port_open "$POSTGRES_HOST" "$POSTGRES_PORT"; then
    printf '[local-run] ERROR: PostgreSQL is not reachable at %s:%s.\n' "$POSTGRES_HOST" "$POSTGRES_PORT" >&2
    missing=1
  fi
  if ! port_open "$REDIS_HOST" "$REDIS_PORT"; then
    printf '[local-run] ERROR: Redis is not reachable at %s:%s.\n' "$REDIS_HOST" "$REDIS_PORT" >&2
    missing=1
  fi
  if [[ "$missing" == "1" ]]; then
    printf '[local-run] Start local PostgreSQL and Redis, or set POSTGRES_HOST/POSTGRES_PORT/REDIS_HOST/REDIS_PORT\n' >&2
    printf '[local-run] to match your user-secrets connection strings. SKIP_DEPENDENCY_CHECK=1 skips this check.\n' >&2
    exit 1
  fi
fi

cleanup() {
  if [[ -n "${API_PID:-}" ]] && kill -0 "$API_PID" 2>/dev/null; then
    kill "$API_PID" 2>/dev/null || true
  fi
  if [[ -n "${BILLING_PID:-}" ]] && kill -0 "$BILLING_PID" 2>/dev/null; then
    kill "$BILLING_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT INT TERM

printf '[local-run] Starting API at %s...\n' "$API_URL"
# --urls overrides the launch profile's applicationUrl; ASPNETCORE_URLS does not.
dotnet run --project Presentation -- --urls "$API_URL" >"$API_LOG_PATH" 2>&1 &
API_PID=$!
printf '[local-run] API PID=%s (logs: %s)\n' "$API_PID" "$API_LOG_PATH"

printf '[local-run] Starting BillingService at %s...\n' "$BILLING_URL"
(
  cd BillingService
  PORT="${BILLING_URL##*:}" npm run dev
) >"$BILLING_LOG_PATH" 2>&1 &
BILLING_PID=$!
printf '[local-run] BillingService PID=%s (logs: %s)\n' "$BILLING_PID" "$BILLING_LOG_PATH"

printf '\n[local-run] Services started. Run smoke in another shell:\n'
printf '  scripts/local/smoke.sh\n\n'
printf '[local-run] Press Ctrl+C to stop both services.\n'

wait "$API_PID" "$BILLING_PID"
