# Local Orchestration Profile (V4 Iteration)

This document defines the deterministic local orchestration workflow for the V4 orchestration-profile iteration.

## Scope

This profile is for **code-ready local validation**. It is not a production-readiness certification.

## Local bootstrap path

From repository root:

```bash
scripts/local/bootstrap.sh
```

Bootstrap performs:

1. `dotnet restore`
2. `dotnet build --no-restore`
3. EF migration apply via `/tmp/dotnet-tools/dotnet-ef database update --project Infrastructure --startup-project Presentation` (when tool exists)
4. `npm ci` in `BillingService/`

### Optional overrides

- `DOTNET_EF_BIN` to point at a different `dotnet-ef` binary path

## Local smoke path

Run services in one shell and smoke in another shell.

Shell A:

```bash
scripts/local/run.sh
```

Shell B:

```bash
scripts/local/smoke.sh
```

Smoke checks currently assert:

1. `GET /health` succeeds for the .NET API
2. `GET /health` succeeds for BillingService
3. `POST /webhooks/provider` accepts a placeholder event payload

## Runtime/environment overrides

`run.sh` supports:

- `API_URL` (default `http://localhost:5000`)
- `BILLING_URL` (default `http://localhost:3001`)
- `API_LOG_PATH` (default `<repo>/.local-api.log`)
- `BILLING_LOG_PATH` (default `<repo>/.local-billing.log`)

`smoke.sh` supports:

- `API_URL` (default `http://localhost:5000`)
- `BILLING_URL` (default `http://localhost:3001`)

## Code-ready local validation vs production readiness

### Code-ready local validation (this iteration)

- deterministic bootstrap/install/build/migration workflow
- deterministic dual-service local startup and teardown behavior
- deterministic smoke checks for health and placeholder webhook acceptance

### Production readiness (not covered by this profile)

- live provider webhook authenticity verification with production secrets and incident handling
- full provider -> BillingService -> .NET callback pipeline hardening in deployed environments
- deployment-verified observability/SLO/paging/DR maturity

## Troubleshooting

- If `bootstrap.sh` reports missing `/tmp/dotnet-tools/dotnet-ef`, run the manual migration command shown in the warning output.
- If smoke fails, inspect `.local-api.log` and `.local-billing.log` for startup errors and rerun once both services report healthy startup.
- If port conflicts occur, set `API_URL` and `BILLING_URL` to alternate ports in both `run.sh` and `smoke.sh`.
