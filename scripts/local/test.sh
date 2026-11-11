#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

DOTNET_EF_BIN="${DOTNET_EF_BIN:-/tmp/dotnet-tools/dotnet-ef}"

printf '\n[local-test] [1/9] dotnet --info\n'
dotnet --info

printf '\n[local-test] [2/9] %s --version\n' "$DOTNET_EF_BIN"
if [[ -x "$DOTNET_EF_BIN" ]]; then
  "$DOTNET_EF_BIN" --version
else
  printf '[local-test] WARNING: %s not found; skipping. Only migration work needs it (set DOTNET_EF_BIN to override).\n' "$DOTNET_EF_BIN"
fi

printf '\n[local-test] [3/9] dotnet restore\n'
dotnet restore

printf '\n[local-test] [4/9] dotnet build --no-restore\n'
dotnet build --no-restore

if ! docker info >/dev/null 2>&1; then
  printf '\n[local-test] WARNING: Docker is not reachable; the Testcontainers PostgreSQL/Redis tests will be skipped, not run.\n'
fi

printf '\n[local-test] [5/9] dotnet test --no-build --verbosity normal\n'
dotnet test --no-build --verbosity normal

printf '\n[local-test] [6/9] BillingService npm ci\n'
(
  cd BillingService
  npm ci
)

printf '\n[local-test] [7/9] BillingService npm run build\n'
(
  cd BillingService
  npm run build
)

printf '\n[local-test] [8/9] BillingService npm run typecheck\n'
(
  cd BillingService
  npm run typecheck
)

printf '\n[local-test] [9/9] BillingService npm test\n'
(
  cd BillingService
  npm test
)
