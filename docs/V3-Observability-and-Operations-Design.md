# V3 Observability and Operations Slice Design (Design Reference for Future Production Telemetry)

Date: April 12, 2026  
Status: Design reference only as of April 26, 2026 (local P1.5 quality gates implemented separately; production telemetry rollout still not implemented)

> Context update (April 26, 2026): local observability quality gates are now implemented and verified under V4 P1.5. This document remains a forward-looking production telemetry design reference (collector/exporter/dashboard/alert/SLO work), not a statement that those production capabilities are currently implemented.

This document defines a lightweight, production-useful observability and operations slice that builds on the **current** foundation already in the repository:

- .NET API `GET /health` and `GET /metrics`
- BillingService `GET /health` and `GET /metrics`
- correlation-id propagation (`X-Correlation-ID`)
- request tracing foundation (`ActivitySource` in .NET, trace-id propagation in BillingService)

## 1) Scope and intent

### Goals
- Keep the first V3 observability slice minimal and deployable.
- Make incidents diagnosable across **both** services.
- Define concrete exporters, dashboards, and alerts without over-design.
- Keep provider-specific billing internals in BillingService and platform state ownership in .NET API.

### Out of scope for this slice
- Full distributed tracing rollout to every internal function.
- High-cardinality business analytics pipelines.
- SIEM-scale log enrichment/redaction redesign.

---

## 2) Proposed target architecture

### 2.1 Signal pipeline
- **Metrics**
  - Instrumentation source:
    - .NET API: existing in-process meters/counters/histograms.
    - BillingService: existing in-process request/activity metrics.
  - Export path (planned):
    - Add OpenTelemetry metrics exporter in both services.
    - Scrape/collect via OTEL Collector.
    - Remote-write to Prometheus-compatible backend (or managed equivalent).
- **Traces**
  - Instrumentation source:
    - .NET API: existing `ActivitySource` (`multi-tenant-saas-api`).
    - BillingService: existing trace-id generation and propagation.
  - Export path (planned):
    - OTLP traces from both services to OTEL Collector.
    - Collector forwards to Jaeger/Tempo-style backend.
- **Logs**
  - Keep structured JSON logs in both services.
  - Ship via runtime log driver/agent (platform-dependent) with correlation-id and trace-id retained.

### 2.2 Minimal environment topology
- OTEL Collector per environment (dev/staging/prod) with:
  - `otlp` receiver
  - `batch` processor
  - exporters:
    - `prometheusremotewrite` (metrics)
    - `otlp` or backend-specific trace exporter (traces)

This keeps service config small while centralizing backend-specific credentials/routing in the collector.

---

## 3) Exporters and config changes (exact planned changes)

## 3.1 .NET API planned config additions

### `Presentation/appsettings.json` (new section)
Add:

```json
"Observability": {
  "OtlpEndpoint": "",
  "ServiceName": "multi-tenant-saas-api",
  "ServiceVersion": "",
  "MetricsExportIntervalMs": 10000,
  "EnableTracing": true,
  "EnableMetrics": true
}
```

### environment variables (preferred in deployed envs)
- `Observability__OtlpEndpoint`
- `Observability__ServiceName`
- `Observability__ServiceVersion`
- `Observability__MetricsExportIntervalMs`
- `Observability__EnableTracing`
- `Observability__EnableMetrics`

## 3.2 BillingService planned config additions

### `BillingService/src/config/env.ts` and README env table (new vars)
- `OTEL_EXPORTER_OTLP_ENDPOINT` (collector endpoint)
- `OTEL_SERVICE_NAME` (default `billing-service`)
- `OTEL_SERVICE_VERSION`
- `OTEL_EXPORT_METRICS` (`true|false`, default `true`)
- `OTEL_EXPORT_TRACES` (`true|false`, default `true`)

## 3.3 Collector config artifact (new doc/config)

Add a new operations doc and sample config:
- `docs/ops/otel-collector.example.yaml` (new)
- `docs/Observability-Runbook.md` (new)

Collector example should include:
- `otlp` receiver for grpc/http
- `batch` processor
- metrics exporter (`prometheusremotewrite` or environment-selected equivalent)
- trace exporter (`otlp` to trace backend)

---

## 4) Dashboard design (v1, lightweight)

## 4.1 API Reliability dashboard (.NET API)
Panels:
1. Request rate by route/status (`http.server.requests`).
2. P95/P99 request latency (`http.server.request.duration`).
3. Active in-flight requests (`http.server.active_requests`).
4. Health status trend from `/health` checks.
5. Database health-check failures count.

Filters:
- environment
- route
- status code class

## 4.2 Billing Operations dashboard (BillingService)
Panels:
1. Request rate and error rate for `/webhooks/provider` and callback-relevant routes.
2. Queue depth, retries, and dead-letter counts (new workflow metrics to emit in V3 impl).
3. Reconciliation drift counts by drift type (new metric family).
4. Worker throughput (processed events/min).
5. Health status trend from `/health`.

Filters:
- environment
- provider
- workflow status (queued/retry/dead-letter)

## 4.3 Cross-service Correlation dashboard
Panels:
1. End-to-end error timeline (API + BillingService).
2. Correlated log volume by `correlationId`.
3. Trace search links (when trace export enabled).

---

## 5) Alert design (production-useful, low-noise)

## 5.1 .NET API alerts
- **High 5xx rate**
  - Condition: 5xx > 2% for 10 minutes.
  - Severity: high.
- **Latency regression**
  - Condition: P95 latency > 1.5s for 15 minutes.
  - Severity: medium.
- **Database health degraded**
  - Condition: DB health check failing for 3 consecutive intervals.
  - Severity: critical.

## 5.2 BillingService alerts
- **Webhook processing failures**
  - Condition: `/webhooks/provider` failure ratio > 3% for 10 minutes.
  - Severity: high.
- **Dead-letter growth**
  - Condition: dead-letter count increases continuously across 3 evaluation windows.
  - Severity: critical.
- **Retry exhaustion spike**
  - Condition: retry-exhausted events > threshold (env-tuned) for 15 minutes.
  - Severity: high.
- **Reconciliation drift spike**
  - Condition: drift detections exceed baseline for 30 minutes.
  - Severity: medium/high depending on drift type.

## 5.3 Cross-service alerts
- **Callback path degradation**
  - Condition: BillingService callback attempts failing + .NET callback endpoint 5xx elevated.
  - Severity: critical.
- **Telemetry pipeline outage**
  - Condition: no metrics/traces received from either service for N minutes while health is up.
  - Severity: high.

---

## 6) Implementation sequence (smallest safe slices)

1. Add OTEL exporter wiring behind flags for .NET API.
2. Add OTEL exporter wiring behind flags for BillingService.
3. Add collector example + observability runbook.
4. Add initial dashboard JSON/templates.
5. Add initial alert rules with staged thresholds in non-prod first.

Each step should be independently shippable and reversible.

---

## 7) Exact documentation changes required when implementation starts

At implementation time, update:

1. `README.md`
   - Add “Observability exporters” section with required env vars and collector dependency.
2. `docs/V3-Implementation-Backlog.md`
   - Add progress checkpoint for this observability slice.
3. `BillingService/README.md`
   - Add OTEL env vars and workflow metric definitions.
4. `docs/Billing-Workflow-Runbook.md`
   - Add alert response playbooks (dead-letter, drift spikes, callback degradation).
5. New: `docs/Observability-Runbook.md`
   - End-to-end telemetry validation and incident triage workflow.

---

## 8) Risks, assumptions, and follow-up

### Assumptions
- Existing correlation-id behavior remains stable in both services.
- Current request-level metrics remain backward compatible when exported.
- Operations team can run a shared OTEL Collector per environment.

### Risks
- Metric naming drift between current custom metrics and OTel conventions.
- High-cardinality tags if tenant identifiers are accidentally emitted in metric labels.
- Alert fatigue if thresholds are not staged in non-prod first.

### Follow-up after v1 slice lands
- Add SLO definitions and error-budget tracking.
- Add trace exemplars tied to latency/error metrics.
- Add runbook automation for dead-letter replay and drift triage.
