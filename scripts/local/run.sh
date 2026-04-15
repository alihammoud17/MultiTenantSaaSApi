#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

API_URL="${API_URL:-http://localhost:5000}"
BILLING_URL="${BILLING_URL:-http://localhost:3001}"
API_LOG_PATH="${API_LOG_PATH:-$REPO_ROOT/.local-api.log}"
BILLING_LOG_PATH="${BILLING_LOG_PATH:-$REPO_ROOT/.local-billing.log}"

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
ASPNETCORE_URLS="$API_URL" dotnet run --project Presentation >"$API_LOG_PATH" 2>&1 &
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
