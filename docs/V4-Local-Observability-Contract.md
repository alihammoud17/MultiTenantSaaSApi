# V4 Local Observability Contract (P1.5)

_Last updated: April 26, 2026_

## 1) Scope and intent

This contract defines the **minimum local observability guarantees** that must remain true during V4 pre-deployment work. It is intentionally limited to behaviors that are already implemented in this repository today.

This contract applies to:
- .NET API (`Presentation` host)
- BillingService (`BillingService` Node host)

This contract does **not** define production telemetry rollout requirements (exporters, dashboards, alert routing, SLOs, or paging policies).

---

## 2) Endpoint availability guarantees (local)

### 2.1 Health endpoints

Required local guarantee:
- Both services expose `GET /health` and return JSON when healthy.

Current implementation details:
- .NET API maps `GET /health` through ASP.NET HealthChecks and returns JSON including `status`, `service`, `correlationId`, `totalDuration`, and per-check details. The API currently includes `self` and `database` checks.
- BillingService handles `GET /health` directly and returns JSON including `status`, `service`, `provider`, `nodeEnv`, `correlationId`, `checks.self`, and `metrics`.

Concrete local gate:
- The local smoke path must fail if either `/health` endpoint is unreachable/non-2xx.

### 2.2 Metrics endpoints

Required local guarantee:
- Both services expose `GET /metrics` and return JSON snapshots of in-process request metrics.

Current implementation details:
- .NET API `GET /metrics` returns `correlationId`, `generatedAtUtc`, and a `metrics` object from `ApiObservability.GetSnapshot()`.
- BillingService `GET /metrics` returns `service`, `correlationId`, `traceId`, `generatedAtUtc`, and `metrics` from `BillingMetrics.snapshot()`.

Concrete local gate:
- `/metrics` must be JSON and include service-level request counters/gauges (`activeRequests`, route/status breakdowns).

---

## 3) Required structured diagnostic fields on critical paths

Critical paths in current local scope:
- inbound HTTP request lifecycle (.NET API + BillingService)
- internal billing callback processing in .NET API
- BillingService workflow processing/retry/dead-letter and reconciliation summaries

### 3.1 Required safe fields

For request lifecycle diagnostics, fields must include:
- `correlationId` (or `.NET` log scope `CorrelationId`)
- `traceId` where currently implemented
- request identity fields: method + route/path
- outcome fields: status code, duration (or equivalent)
- timestamp (BillingService JSON logger provides `timestamp`)

For billing workflow diagnostics (BillingService), fields must include where applicable:
- `eventId`
- `tenantId`
- `correlationId`
- attempt/retry state (`attempt`, `maxAttempts`, retry delay) and terminal reason (`error`) for failure paths

For outbound webhook/local callback diagnosability (.NET side currently implemented):
- persisted delivery diagnostics remain available via `LastAttemptAtUtc`, `NextAttemptAtUtc`, `LastHttpStatusCode`, `LastError`, and `AttemptCount` state transitions (as documented in existing outbound webhook docs/tests)

### 3.2 Minimum continuity guarantees

- Correlation continuity:
  - .NET API must honor incoming `X-Correlation-ID` when provided; otherwise generate a fallback correlation id.
  - .NET API must echo `X-Correlation-ID` in response headers and include it in `/health` and `/metrics` payloads.
  - BillingService must honor incoming `x-correlation-id` when provided; otherwise generate a fallback correlation id.
  - BillingService must echo `x-correlation-id` in response headers and include correlation id in `/health`, `/metrics`, and webhook responses.
- Trace continuity (only where already implemented):
  - .NET API creates an `Activity` per request and tags correlation/method/path/status; log activity tracking is enabled.
  - BillingService emits and echoes `traceId` for observed requests, but currently derives it from `correlationId` (not full W3C trace-context interoperability).
  - No additional cross-service distributed tracing requirements are imposed by this contract beyond current behavior.

---

## 4) Failure-state diagnosability expectations (local)

Minimum guarantee:
- Failures must be diagnosable locally from structured responses/logs and persisted workflow state without requiring hidden context.

Concretely:
- Request failures must include `correlationId` in API responses already doing so (for example BillingService 500/404 bodies and .NET health/metrics payload correlation fields).
- BillingService unhandled request exceptions must log structured error records with `message`, `correlationId`, `traceId`, `route`, and `method`.
- Workflow retry/dead-letter paths must preserve inspectable attempt counters and last error details in logs/state.

---

## 5) Sensitive-data minimization rules (required)

### 5.1 Forbidden fields/content

The following must never be logged, persisted as diagnostic metadata, or asserted in tests as expected observability content:
- raw JWTs
- refresh tokens
- bearer tokens
- API keys
- secret configuration values
- webhook signing secrets
- internal callback secrets
- password reset secrets/tokens
- MFA enrollment secrets/tokens
- uncontrolled full raw payload dumps

### 5.2 Required safe handling

- Prefer non-secret identifiers and bounded metadata only (tenantId, eventId, correlationId, status, durations, route, method).
- If payload-level troubleshooting is required, log narrowly scoped derived fields (for example ids/state flags) rather than whole payloads.
- Error records must avoid embedding secret-bearing request headers or bodies.

---

## 6) Known implementation gaps / ambiguities revealed by this contract

1. **Structured-field quality gates are now partial, not repository-wide**
   - Deterministic tests now cover representative high-value paths (BillingService request lifecycle logs, BillingService workflow dead-letter diagnostics, .NET internal billing callback correlation continuity, and .NET outbound webhook retry-state diagnostics).
   - Remaining work is to broaden this into a repository-wide schema gate for all sensitive flows.

2. **BillingService trace id is correlation-derived, not distributed trace-context based**
   - Current `traceId` continuity is local/request-scoped and deterministic, but not equivalent to full traceparent propagation.

3. **Potential payload-overlogging risk if future changes log raw webhook bodies/errors directly**
   - Current code avoids explicit raw payload logging on core request paths; this contract now makes that prohibition explicit to prevent regressions.

4. **.NET request log schema is partially implicit via logging scope/activity tracking**
   - Correlation and trace enrichment is configured, but not yet codified by automated schema assertions in tests.

---

## 7) Practical verification checklist and current automated coverage

The first deterministic automated coverage slices for this checklist are implemented (April 26, 2026) across:
- `.NET` integration tests for `GET /health` and `GET /metrics`
- BillingService tests for `GET /health` and `GET /metrics`
- local smoke gate checks for `/health` + `/metrics` JSON reachability on both services

Automated checks currently validate at least:
- `/health` + `/metrics` endpoint availability and JSON shape in both services.
- required top-level observability fields remain present for current repo usage.
- bounded sensitive-data absence checks in health/metrics payloads (`secret`/`password`/`token` classes are not expected content).

Still pending in later slices:
- correlation-id echo/continuity assertions for every path on both services.
- broader required safe-field assertions for additional log events beyond the representative covered paths.
- deeper absence checks for forbidden sensitive fields in log outputs and persisted diagnostic state.
- trace continuity assertions beyond currently implemented request-local behavior.
