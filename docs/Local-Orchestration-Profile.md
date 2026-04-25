# Local Orchestration Profile (V4 Iteration)

This document defines the deterministic local orchestration workflow for the V4 orchestration-profile iteration.

## Scope

This profile is for **code-ready local validation**. It is not a production-readiness certification.

## Local bootstrap/reset/seed path

From repository root:

```bash
scripts/local/bootstrap.sh
# optional clean-state reset:
scripts/local/reset.sh
# explicit seed step:
scripts/local/seed.sh
```

Bootstrap performs:

1. `dotnet restore`
2. `dotnet build --no-restore`
3. EF migration apply via `/tmp/dotnet-tools/dotnet-ef database update --project Infrastructure --startup-project Presentation` (when tool exists)
4. `npm ci` in `BillingService/`

Reset performs:

1. remove local orchestration artifacts (`.local-api.log`, `.local-billing.log`, BillingService durable workflow state file)
2. drop the local DB and re-apply migrations via `/tmp/dotnet-tools/dotnet-ef` when the tool exists
3. print explicit manual `dotnet ef` reset commands when the tool is unavailable

Seed performs:

1. `dotnet-ef database update --project Infrastructure --startup-project Presentation` when tooling exists
2. explicit manual-boundary output that scenario/demo tenant seed packs are still manual in this iteration

### Optional overrides

- `DOTNET_EF_BIN` to point at a different `dotnet-ef` binary path
- `WORKFLOW_STATE_PATH` to override BillingService durable workflow state file location for `reset.sh`

## Local smoke/test path

Run services in one shell and smoke in another shell.

Shell A:

```bash
scripts/local/run.sh
```

Shell B:

```bash
scripts/local/smoke.sh
scripts/local/test.sh
```

Smoke checks currently assert:

1. `GET /health` succeeds for the .NET API
2. `GET /health` succeeds for BillingService
3. `POST /webhooks/provider` accepts a placeholder event payload

Test checks execute this standardized cross-service sequence:

1. `dotnet --info`
2. `/tmp/dotnet-tools/dotnet-ef --version`
3. `dotnet restore`
4. `dotnet build --no-restore`
5. `dotnet test --no-build --verbosity normal`
6. `npm ci` in `BillingService/`
7. `npm run build` in `BillingService/`
8. `npm test` in `BillingService/`

### Expected execution order for local validation

Use this order to keep triage deterministic:

1. `scripts/local/bootstrap.sh`
2. `scripts/local/seed.sh`
3. `scripts/local/run.sh`
4. `scripts/local/smoke.sh` (from a second shell while `run.sh` is active)
5. `scripts/local/test.sh`

If one step fails, fix that step before progressing to downstream checks.

## Runtime/environment overrides

`run.sh` supports:

- `API_URL` (default `http://localhost:5000`)
- `BILLING_URL` (default `http://localhost:3001`)
- `API_LOG_PATH` (default `<repo>/.local-api.log`)
- `BILLING_LOG_PATH` (default `<repo>/.local-billing.log`)

`smoke.sh` supports:

- `API_URL` (default `http://localhost:5000`)
- `BILLING_URL` (default `http://localhost:3001`)

`reset.sh` supports:

- `DOTNET_EF_BIN` (default `/tmp/dotnet-tools/dotnet-ef`)
- `API_LOG_PATH` / `BILLING_LOG_PATH` (same defaults as `run.sh`)
- `WORKFLOW_STATE_PATH` (default `<repo>/BillingService/.billing-workflow-state.json`)

## Expected local runtimes (non-binding guidance)

These ranges are guidance for failure triage (hardware/cache dependent):

- `bootstrap.sh`: ~2-6 minutes
- `reset.sh`: ~30-90 seconds
- `seed.sh`: ~10-30 seconds when migrations are current
- `smoke.sh`: under 10 seconds after services are healthy
- `test.sh`: ~3-10 minutes

## Code-ready local validation vs production readiness

### Code-ready local validation (this iteration)

- deterministic bootstrap/install/build/migration workflow
- deterministic dual-service local startup and teardown behavior
- deterministic smoke checks for health and placeholder webhook acceptance
- deterministic standardized reset/seed/test wrappers with explicit manual boundaries

### Production readiness (not covered by this profile)

- live provider webhook authenticity verification with production secrets and incident handling
- full provider -> BillingService -> .NET callback pipeline hardening in deployed environments
- deployment-verified observability/SLO/paging/DR maturity

## Troubleshooting triage paths

### 1) Dependency install failure

Common surfaces:

- `bootstrap.sh` at `dotnet restore` or `npm ci`
- `test.sh` at .NET restore or BillingService install/build phases

Next steps:

1. Re-run the failing command directly to get focused output:
   - `dotnet restore` (repo root)
   - `cd BillingService && npm ci`
2. Verify local prerequisites from repo docs (.NET SDK, Node, PostgreSQL, Redis).
3. After direct command success, rerun the wrapper script that failed.

### 2) Migration/tooling failure

Common surfaces:

- `bootstrap.sh` migration apply stage
- `reset.sh` drop/update stages
- `seed.sh` update stage

Next steps:

1. If `/tmp/dotnet-tools/dotnet-ef` is missing, run the manual command the script prints:
   - `dotnet ef database update --project Infrastructure --startup-project Presentation`
   - for reset: also run `dotnet ef database drop --force --project Infrastructure --startup-project Presentation`
2. If tool exists but command fails, validate `Presentation` DB secrets/connectivity.
3. Re-run the same EF command manually, then rerun the local script.

### 3) Service start failure

Common surface:

- `run.sh` where one service exits quickly or never becomes reachable

Next steps:

1. Inspect `.local-api.log` and `.local-billing.log`.
2. Resolve the first startup exception in the failing service.
3. Restart with `scripts/local/run.sh` before running smoke checks.

### 4) Port/config mismatch

Common surface:

- `smoke.sh` checks wrong URLs/ports while services are running on different values

Next steps:

1. Ensure `API_URL` and `BILLING_URL` are set consistently for both `run.sh` and `smoke.sh`.
2. If using alternate ports, export the same values in each shell session.
3. Re-run smoke once endpoints align.

### 5) Smoke-test failure

Common surface:

- `smoke.sh` fails API health, BillingService health, or webhook acceptance call

Next steps:

1. Confirm `run.sh` is still running and did not terminate.
2. Re-check `.local-api.log` and `.local-billing.log` for readiness or startup errors.
3. Rerun `smoke.sh` only after both `/health` endpoints are reachable.

### 6) Test-suite failure

Common surface:

- `test.sh` step failures in .NET or BillingService checks

Next steps:

1. Use the printed step number to rerun the exact failing command directly.
2. Fix the failure in that layer first (`dotnet test`, `npm run build`, or `npm test`).
3. Re-run full `scripts/local/test.sh` to verify the entire sequence is green.

## Current local workflow ambiguities (follow-up cleanup)

- `seed.sh` intentionally does not automate scenario/demo tenant data; manual API-flow seeding remains required.
- `bootstrap.sh`/`seed.sh` can continue without `/tmp/dotnet-tools/dotnet-ef`; this preserves progress but leaves migration execution partially manual.
- `smoke.sh` validates readiness and placeholder webhook acceptance only; it does not validate provider-authentic webhook signatures or live provider-to-.NET callback E2E behavior.
