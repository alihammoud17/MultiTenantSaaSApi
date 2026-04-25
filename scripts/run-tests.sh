#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo "[run-tests] Delegating to standardized local validation script."
"$REPO_ROOT/scripts/local/test.sh"
