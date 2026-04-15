#!/usr/bin/env bash
set -euo pipefail

API_URL="${API_URL:-http://localhost:5000}"
BILLING_URL="${BILLING_URL:-http://localhost:3001}"

echo "[local-smoke] Checking API health at $API_URL/health"
curl -fsS "$API_URL/health" >/dev/null

echo "[local-smoke] Checking BillingService health at $BILLING_URL/health"
curl -fsS "$BILLING_URL/health" >/dev/null

echo "[local-smoke] Checking BillingService provider webhook acceptance"
curl -fsS -X POST "$BILLING_URL/webhooks/provider" \
  -H 'Content-Type: application/json' \
  -d '{"eventId":"local-smoke-event","eventType":"subscription.renewed"}' >/dev/null

echo "[local-smoke] Smoke checks passed."
