# Billing Workflow Runbook (Durable Workflow Iteration)

This runbook documents operational procedures for the current durable workflow iteration in `BillingService`.

> Scope note: this iteration is pre-live. Durable queueing, retry, dead-letter, and reconciliation scaffolding exist, but live provider integration and authenticated callback delivery to the .NET API are still pending.

## 1. Purpose and boundaries

`BillingService` owns provider-facing workflow durability and reconciliation behavior. The .NET API remains the system of record for tenant/business data and internal subscription state.

During this iteration, operators should treat `BillingService` as:

- a durable workflow engine scaffold
- a replay-safe event processing boundary
- a reconciliation drift detection scaffold

Operators should **not** treat it as a complete billing control plane yet.

## 2. Key runtime controls

Current environment variables used for durability and reconciliation operations:

- `WORKFLOW_STATE_PATH` - JSON state file used for queue, retry metadata, deduplication history, and dead-letter records
- `WORKFLOW_MAX_ATTEMPTS` - maximum attempts before an item is dead-lettered
- `WORKFLOW_INITIAL_BACKOFF_MS` - initial retry backoff
- `WORKFLOW_MAX_BACKOFF_MS` - maximum retry backoff cap
- `WORKFLOW_POLL_INTERVAL_MS` - worker polling interval
- `RECONCILIATION_INTERVAL_MS` - reconciliation polling interval

### Recommended baseline values for non-production environments

- `WORKFLOW_MAX_ATTEMPTS=3`
- `WORKFLOW_INITIAL_BACKOFF_MS=1000`
- `WORKFLOW_MAX_BACKOFF_MS=30000`
- `WORKFLOW_POLL_INTERVAL_MS=2000`
- `RECONCILIATION_INTERVAL_MS=300000`

## 3. Pre-start checklist

Before starting `BillingService`:

1. Confirm the state file path is writable and persistent for the current environment.
2. Confirm no secret values are echoed in startup scripts or logs.
3. Confirm health and metrics endpoints are reachable from your operator network.
4. Confirm runbook consumers understand that callback delivery and provider verification are not live yet.

## 4. Startup and smoke verification

From repository root:

```bash
cd BillingService
npm install
npm run dev
```

Basic verification:

- `GET /health` should return healthy status with request/correlation context.
- `GET /metrics` should return request counters and activity snapshot.
- Workflow state file should be created/updated at `WORKFLOW_STATE_PATH` after queue activity.

## 5. Dead-letter triage procedure

When an item reaches `WORKFLOW_MAX_ATTEMPTS`:

1. Inspect structured logs for correlation id, event id, and rejection reason.
2. Determine failure class:
   - transient delivery failure
   - permanent mapping/validation failure
   - code or configuration defect
3. Fix the underlying issue first.
4. Replay only impacted event ids once the fix is in place.
5. Verify replay does not create duplicate side effects (dedup by event id must hold).

## 6. Replay and duplicate-safety procedure

Use replay carefully and only after root-cause confirmation:

1. Identify the normalized `eventId`.
2. Confirm whether it is already marked processed in workflow state.
3. If already processed, do not force-requeue unless intentionally testing idempotency behavior.
4. If dead-lettered due to transient causes, requeue once and observe retry progression.
5. Keep replay batches small and correlated for easier rollback analysis.

## 7. Reconciliation drift handling

Current reconciliation behavior compares provider snapshots and internal snapshots and enqueues deterministic correction intents.

When drift is detected:

1. Validate the mismatch is real (not stale data source timing).
2. Classify drift type:
   - missing internal event application
   - stale provider snapshot
   - mapping mismatch
3. Verify the correction action type and target identifiers.
4. Track the correction through workflow state transitions.
5. Document unresolved drift in incident notes and backlog if manual intervention is required.

## 8. Logging and observability expectations

Operators should expect structured logs for:

- event receipt/queueing
- retry attempts and backoff delays
- dead-letter transitions
- reconciliation summary and drift classification

Avoid logging secrets, signing material, or full sensitive payload bodies.

## 9. Known limitations in this iteration

- No live provider SDK integration.
- No production webhook signature verification flow.
- No live authenticated callback dispatch to .NET API in default runtime wiring.
- Reconciliation data sources are scaffolded and require live adapters.

## 10. Escalation guidance

Escalate to engineering when any of the following occur:

- repeated dead-letter growth over multiple reconciliation intervals
- persistent drift after replay and retry checks
- state-file corruption or repeated parse/load failures
- unexpected duplicate side effects despite stable `eventId`

When escalating, include:

- affected event ids
- correlation ids
- first-seen/last-seen timestamps
- failure class and mitigation attempts

## 11. Follow-up work expected after this iteration

1. Wire authenticated callback publisher to `.NET` internal billing endpoint.
2. Integrate first live provider adapter with signature verification.
3. Replace scaffolded data readers used by reconciliation with live provider/.NET readers.
4. Add explicit operational metrics/alerts for dead-letter counts, retry exhaustion, and reconciliation drift rates.
