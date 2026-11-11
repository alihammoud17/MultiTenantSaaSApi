#!/usr/bin/env bash
# PostToolUse hook: when one side of the internal billing contract changes,
# remind Claude that the producer test, consumer test, and contract doc move together.
set -euo pipefail

file_path="$(jq -r '.tool_input.file_path // empty')"

case "$file_path" in
  */docs/contracts.md | \
  */BillingService/tests/billingCallbackContract.test.ts | \
  */Tests/Integration/CrossServiceBillingContractTests.cs)
    cat >&2 <<EOF
Billing contract file changed: ${file_path#"$CLAUDE_PROJECT_DIR"/}
The internal billing contract must change together across:
  - docs/contracts.md (contract definition)
  - BillingService/tests/billingCallbackContract.test.ts (producer conformance)
  - Tests/Integration/CrossServiceBillingContractTests.cs (consumer conformance)
Check that the other two are consistent with this edit, and validate both the producer and consumer suites.
EOF
    exit 2
    ;;
esac

exit 0
