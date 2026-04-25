#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

printf '\n[local-test] [1/8] dotnet --info\n'
dotnet --info

printf '\n[local-test] [2/8] /tmp/dotnet-tools/dotnet-ef --version\n'
/tmp/dotnet-tools/dotnet-ef --version

printf '\n[local-test] [3/8] dotnet restore\n'
dotnet restore

printf '\n[local-test] [4/8] dotnet build --no-restore\n'
dotnet build --no-restore

printf '\n[local-test] [5/8] dotnet test --no-build --verbosity normal\n'
dotnet test --no-build --verbosity normal

printf '\n[local-test] [6/8] BillingService npm ci\n'
(
  cd BillingService
  npm ci
)

printf '\n[local-test] [7/8] BillingService npm run build\n'
(
  cd BillingService
  npm run build
)

printf '\n[local-test] [8/8] BillingService npm test\n'
(
  cd BillingService
  npm test
)
