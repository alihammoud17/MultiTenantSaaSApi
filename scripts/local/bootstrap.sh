#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

DOTNET_EF_BIN="${DOTNET_EF_BIN:-/tmp/dotnet-tools/dotnet-ef}"

printf '\n[local-bootstrap] Using repo root: %s\n' "$REPO_ROOT"
printf '[local-bootstrap] [1/4] Restoring .NET dependencies...\n'
dotnet restore

printf '[local-bootstrap] [2/4] Building .NET solution...\n'
dotnet build --no-restore

if [[ -x "$DOTNET_EF_BIN" ]]; then
  printf '[local-bootstrap] [3/4] Applying EF migrations with %s...\n' "$DOTNET_EF_BIN"
  "$DOTNET_EF_BIN" database update --project Infrastructure --startup-project Presentation
else
  printf '[local-bootstrap] [3/4] WARNING: %s not found; skipping migration apply.\n' "$DOTNET_EF_BIN"
  printf '[local-bootstrap] Manual step: dotnet ef database update --project Infrastructure --startup-project Presentation\n'
fi

printf '[local-bootstrap] [4/4] Installing BillingService dependencies (npm ci)...\n'
(
  cd BillingService
  npm ci
)

printf '[local-bootstrap] Bootstrap complete.\n\n'
printf 'Next steps:\n'
printf '  1) scripts/local/seed.sh      # explicit seed/migration refresh step\n'
printf '  2) scripts/local/run.sh\n'
printf '  3) scripts/local/smoke.sh\n'
printf '  4) scripts/local/test.sh      # full verification path\n\n'
