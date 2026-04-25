#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

DOTNET_EF_BIN="${DOTNET_EF_BIN:-/tmp/dotnet-tools/dotnet-ef}"

printf '\n[local-seed] Using repo root: %s\n' "$REPO_ROOT"

if [[ -x "$DOTNET_EF_BIN" ]]; then
  printf '[local-seed] [1/2] Applying migrations to ensure model-managed seed data is current...\n'
  "$DOTNET_EF_BIN" database update --project Infrastructure --startup-project Presentation
else
  printf '[local-seed] [1/2] WARNING: %s not found; cannot automate EF migration/seed refresh.\n' "$DOTNET_EF_BIN"
  printf '[local-seed] Manual step: dotnet ef database update --project Infrastructure --startup-project Presentation\n'
fi

printf '[local-seed] [2/2] Manual boundary: scenario/demo tenant seed packs are not automated in P1.2.\n'
printf '[local-seed] Manual step: create tenant/demo data through existing API flows (register/login/admin) when needed.\n'
