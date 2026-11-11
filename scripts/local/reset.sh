#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

DOTNET_EF_BIN="${DOTNET_EF_BIN:-/tmp/dotnet-tools/dotnet-ef}"
API_LOG_PATH="${API_LOG_PATH:-$REPO_ROOT/.local-api.log}"
BILLING_LOG_PATH="${BILLING_LOG_PATH:-$REPO_ROOT/.local-billing.log}"
WORKFLOW_STATE_PATH="${WORKFLOW_STATE_PATH:-$REPO_ROOT/BillingService/.billing-workflow-state.json}"

printf '\n[local-reset] Using repo root: %s\n' "$REPO_ROOT"
printf '[local-reset] [1/3] Removing local orchestration artifacts...\n'
rm -f "$API_LOG_PATH" "$BILLING_LOG_PATH"
rm -f "$WORKFLOW_STATE_PATH"

if [[ -x "$DOTNET_EF_BIN" ]]; then
  printf '[local-reset] [2/3] Dropping local database via %s...\n' "$DOTNET_EF_BIN"
  "$DOTNET_EF_BIN" database drop --force --project Infrastructure --startup-project Presentation

  printf '[local-reset] [3/3] Re-applying migrations...\n'
  "$DOTNET_EF_BIN" database update --project Infrastructure --startup-project Presentation
else
  printf '[local-reset] [2/3] WARNING: %s not found; skipping automated DB reset.\n' "$DOTNET_EF_BIN"
  printf '[local-reset] [3/3] Manual step: dotnet ef database drop --force --project Infrastructure --startup-project Presentation\n'
  printf '[local-reset] Manual step: dotnet ef database update --project Infrastructure --startup-project Presentation\n'
fi

printf '[local-reset] Reset complete. Next step: scripts/local/seed.sh\n'
