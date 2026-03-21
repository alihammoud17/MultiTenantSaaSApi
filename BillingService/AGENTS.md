# AGENTS.md

## BillingService purpose
This folder contains the Node.js / TypeScript billing service for the multi-tenant SaaS platform.

BillingService is responsible for:
- billing provider integration
- provider webhook ingestion and verification
- provider-to-internal billing event normalization
- retry-safe billing workflow processing
- reconciliation with provider state
- secure communication with the .NET API

BillingService is not the system of record for tenant/business domain data.

## Service boundary rules
- Do not move tenant enforcement or RBAC logic into BillingService.
- Do not let BillingService become a second source of truth for unrelated platform state.
- Keep provider-specific code isolated behind adapters.
- Do not expose raw provider payloads as internal platform contracts without normalization.
- Internal events/callbacks sent to the .NET API must be authenticated and traceable.

## Current V3 focus for this service
The next work in this folder should generally align with:
- live provider integration
- webhook verification
- durable retry/replay-safe processing
- reconciliation jobs
- customer billing support
- operational observability

## Folder-level implementation style
For any non-trivial task:
1. inspect the current BillingService code and README first
2. identify the exact files to change
3. plan the smallest safe slice
4. preserve structure unless there is a strong reason to improve it
5. add or update tests
6. update docs for the iteration
7. summarize changes and remaining gaps

## Provider integration rules
- Support one provider at a time for initial implementation slices unless explicitly requested otherwise.
- Verify webhook authenticity before processing.
- Normalize provider payloads into internal event models.
- Keep provider status mapping explicit.
- Track external event identifiers for idempotency and replay protection.
- Make duplicate delivery safe.
- Avoid coupling business decisions directly to raw provider event types.

## Internal callback / contract rules
- Use the documented internal billing contract when communicating with the .NET API.
- If the contract changes, update docs/Internal-Billing-Contract.md.
- Internal callbacks must be authenticated.
- Internal callbacks must include enough information for:
  - tenant identification
  - subscription identification
  - event typing
  - traceability
  - idempotent processing
- Do not silently ignore mapping failures; log them clearly and handle them safely.

## Workflow and job rules
- Long-running or retrying billing logic must stay out of HTTP route handlers.
- Prefer dedicated handlers/services for:
  - webhook processing
  - event normalization
  - callback dispatch
  - reconciliation
  - retries and dead-letter handling
- Every retryable workflow should be safe against duplicate delivery.
- Reconciliation jobs must be safe to rerun.

## Error-handling rules
- Fail closed for invalid signatures or invalid authenticated internal requests.
- Distinguish clearly between:
  - invalid external input
  - transient downstream failure
  - permanent mapping/business rejection
- Preserve enough context in logs for debugging without exposing secrets.

## Testing expectations
Add or update tests for:
- valid webhook acceptance
- invalid signature rejection
- duplicate event idempotency
- callback retry behavior
- mapping failures
- replay handling
- reconciliation behavior
- provider adapter behavior
- config validation where practical

## Validation
Run the relevant project commands after changes.

If package scripts already exist, use them.
If package scripts change, update the README.

Typical expectations:
- install dependencies
- build the service
- run tests

Document the exact commands in BillingService/README.md if they are missing or changed.

## Required documentation workflow
Whenever work in this folder is completed, review and update as applicable:
- BillingService/README.md
- README.md if platform-level behavior changed
- docs/Internal-Billing-Contract.md if internal contracts changed
- docs/V3-Implementation-Backlog.md
- any new runbook or feature-specific doc under docs/

Do not claim live provider support, reconciliation, invoices, or customer self-service unless the code actually implements them.

## Configuration and secrets
- Never hardcode provider keys, signing secrets, callback secrets, or tokens.
- Use environment variables or the existing config conventions.
- Validate required config at startup where practical.
- Do not log secrets or sensitive raw payload contents unnecessarily.
- Document any newly required environment variables.

## Observability expectations
- emit structured logs
- include correlation IDs / trace identifiers where possible
- log provider event receipt, verification result, normalization result, callback result, retry state, and reconciliation outcomes
- expose health and metrics consistently
- update docs when new metrics or runbooks are introduced

## Coding guidelines
- Keep routes thin.
- Keep provider adapters, handlers, workflows, config, and transport concerns separated.
- Avoid broad refactors unless explicitly requested.
- Prefer explicit types and predictable data mapping.
- Preserve local developer usability.

## Done when
A BillingService task is complete only when:
- the requested behavior is implemented
- provider/service boundaries remain clean
- relevant build/tests pass
- docs are updated for the iteration
- changed files are summarized
- assumptions, risks, and follow-up work are listed explicitly