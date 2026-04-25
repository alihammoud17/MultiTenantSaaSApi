#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

usage() {
  cat <<'USAGE'
Usage:
  scripts/dev.sh <command>

Common local developer-loop commands:
  bootstrap   Prepare local dependencies/build/migrations
  reset       Clean logs/workflow state + reset local database
  seed        Re-apply migrations and print manual seed boundary
  run         Start API and BillingService (foreground)
  smoke       Run local readiness smoke checks
  test        Run full .NET + BillingService validation checks
  help        Show this command index

Notes:
- This wrapper only dispatches to scripts/local/*.sh and does not hide behavior.
- You can continue to run underlying scripts directly.
USAGE
}

if [[ $# -eq 0 ]]; then
  usage
  exit 1
fi

command="$1"
shift || true

case "$command" in
  bootstrap|reset|seed|run|smoke|test)
    exec "$REPO_ROOT/scripts/local/$command.sh" "$@"
    ;;
  help|-h|--help)
    usage
    ;;
  *)
    echo "[dev] Unknown command: $command" >&2
    echo >&2
    usage >&2
    exit 1
    ;;
esac
