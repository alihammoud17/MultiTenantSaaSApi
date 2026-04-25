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

## Troubleshooting

- If `bootstrap.sh` reports missing `/tmp/dotnet-tools/dotnet-ef`, run the manual migration command shown in the warning output.
- If `reset.sh` cannot drop the database, verify `Presentation` user secrets and DB connectivity, then run the printed manual `dotnet ef` reset commands.
- If `seed.sh` succeeds but scenario data is missing, that is expected in this slice: create demo tenant data via existing API flows manually.
- If smoke fails, inspect `.local-api.log` and `.local-billing.log` for startup errors and rerun once both services report healthy startup.
- If port conflicts occur, set `API_URL` and `BILLING_URL` to alternate ports in both `run.sh` and `smoke.sh`.
- If `test.sh` fails in BillingService steps, rerun `npm ci` in `BillingService/` and check Node version alignment (`>=22`).
