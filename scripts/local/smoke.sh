#!/usr/bin/env bash
set -euo pipefail

API_URL="${API_URL:-http://localhost:5000}"
BILLING_URL="${BILLING_URL:-http://localhost:3001}"

check_json_endpoint() {
  local endpoint="$1"
  local tmp_body
  tmp_body="$(mktemp)"

  local curl_meta
  curl_meta="$(curl -fsS -o "$tmp_body" -w '%{http_code}|%{content_type}' "$endpoint")"
  local status_code="${curl_meta%%|*}"
  local content_type="${curl_meta#*|}"

  if [[ "$status_code" != "200" ]]; then
    echo "[local-smoke] Expected 200 from $endpoint, got $status_code"
    rm -f "$tmp_body"
    return 1
  fi

  if [[ "$content_type" != application/json* ]]; then
    echo "[local-smoke] Expected JSON response from $endpoint, got content-type: $content_type"
    rm -f "$tmp_body"
    return 1
  fi

  python3 - "$tmp_body" <<'PY'
import json
import pathlib
import sys

raw = pathlib.Path(sys.argv[1]).read_text(encoding="utf-8")
json.loads(raw)
PY

  rm -f "$tmp_body"
}

echo "[local-smoke] Checking API health at $API_URL/health"
check_json_endpoint "$API_URL/health"

echo "[local-smoke] Checking API metrics at $API_URL/metrics"
check_json_endpoint "$API_URL/metrics"

echo "[local-smoke] Checking BillingService health at $BILLING_URL/health"
check_json_endpoint "$BILLING_URL/health"

echo "[local-smoke] Checking BillingService metrics at $BILLING_URL/metrics"
check_json_endpoint "$BILLING_URL/metrics"

echo "[local-smoke] Checking BillingService provider webhook acceptance"
curl -fsS -X POST "$BILLING_URL/webhooks/provider" \
  -H 'Content-Type: application/json' \
  -d '{"eventId":"local-smoke-event","eventType":"subscription.renewed"}' >/dev/null

echo "[local-smoke] Smoke checks passed."
