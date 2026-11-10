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

  node -e 'JSON.parse(require("node:fs").readFileSync(process.argv[1], "utf8"))' "$tmp_body"

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

# The placeholder adapter answers every webhook with 202 and accepted:false,
# so this proves the endpoint is reachable, not that events are processed.
echo "[local-smoke] Checking BillingService provider webhook endpoint reachability (placeholder rejects; expects 202)"
webhook_status="$(curl -sS -o /dev/null -w '%{http_code}' -X POST "$BILLING_URL/webhooks/provider" \
  -H 'Content-Type: application/json' \
  -d '{"eventId":"local-smoke-event","eventType":"subscription.renewed"}')"
if [[ "$webhook_status" != "202" ]]; then
  echo "[local-smoke] Expected 202 from $BILLING_URL/webhooks/provider, got $webhook_status"
  exit 1
fi

echo "[local-smoke] Smoke checks passed."
