# Historical Documentation Archive

Files in this directory are retained for project history and due-diligence traceability. They are **non-authoritative** and may describe completed plans, superseded designs, outdated paths, or future capabilities that were never implemented. Use the active documents in the repository root and `docs/` for current behavior.

| Archived file | Current replacement | Notes |
| --- | --- | --- |
| `BillingService/technical-documentation.md` | [`../architecture.md`](../architecture.md), [`../../BillingService/README.md`](../../BillingService/README.md) | Historical file-by-file implementation snapshot. |
| `Billing-Workflow-Runbook.md` | [`../operations.md`](../operations.md) | Workflow configuration, replay, dead-letter, reconciliation, and limitations consolidated. |
| `Deployment-Readiness-Backlog.md` | [`../operations.md`](../operations.md), [`../decisions.md`](../decisions.md) | Unresolved deployment choices remain in the decisions file. |
| `Entitlements-Model.md` | [`../architecture.md`](../architecture.md) | Current ownership, precedence, enforcement, and limitations consolidated. |
| `Identity-and-Security.md` | [`../architecture.md`](../architecture.md), [`../operations.md`](../operations.md) | Current identity design and configuration consolidated. |
| `Internal-Billing-Contract.md` | [`../contracts.md`](../contracts.md) | Current signed callback contract consolidated. |
| `Local-Orchestration-Profile.md` | [`../operations.md`](../operations.md) | Current local workflow and troubleshooting consolidated. |
| `Outbound-Webhook-Contract.md` | [`../contracts.md`](../contracts.md) | Current outbound delivery contract consolidated. |
| `Outbound-Webhook-Endpoint-Management-Runbook.md` | [`../operations.md`](../operations.md) | Current management and rotation operations consolidated. |
| `Outbound-Webhooks.md` | [`../architecture.md`](../architecture.md), [`../contracts.md`](../contracts.md) | Current architecture and wire contract consolidated. |
| `Usage-Analytics.md` | [`../architecture.md`](../architecture.md) | Current ownership and isolation summary consolidated. |
| `V1-Project-Documentation.md` | [`../CHANGELOG.md`](../CHANGELOG.md), [`../architecture.md`](../architecture.md) | Historical V1 snapshot. |
| `V2-Implementation-Backlog.md` | [`../CHANGELOG.md`](../CHANGELOG.md) | Superseded V2 plan. |
| `V3-Implementation-Backlog.md` | [`../CHANGELOG.md`](../CHANGELOG.md) | Closed V3 plan. |
| `V3-Observability-and-Operations-Design.md` | [`../decisions.md`](../decisions.md), [`../operations.md`](../operations.md) | Forward-looking production telemetry design; exporter stack was not implemented. |
| `V4-AuthController-Application-Boundary-Design.md` | [`../architecture.md`](../architecture.md), [`../CHANGELOG.md`](../CHANGELOG.md) | Pre-implementation design superseded by `AuthOrchestrationService`. |
| `V4-CrossService-Contract-Test-Design.md` | [`../contracts.md`](../contracts.md), [`../CHANGELOG.md`](../CHANGELOG.md) | Implemented baseline design. |
| `V4-Entitlement-Matrix-Test-Design.md` | [`../architecture.md`](../architecture.md), [`../CHANGELOG.md`](../CHANGELOG.md) | Implemented test-design snapshot. |
| `V4-Implementation-Backlog.md` | [`../architecture.md`](../architecture.md), [`../operations.md`](../operations.md), [`../CHANGELOG.md`](../CHANGELOG.md) | Historical execution ledger, not an active source of truth. |
| `V4-Local-Observability-Contract.md` | [`../operations.md`](../operations.md) | Local observability guarantees and limitations consolidated. |
| `V4-TechDebt-Planning.md` | [`../CHANGELOG.md`](../CHANGELOG.md), [`../decisions.md`](../decisions.md) | Completed planning ledger and residual decisions. |
| `versioning.md` | [`../decisions.md`](../decisions.md) | Versioning policy consolidated. |

Archive files should not be edited to keep them synchronized with code. If historical context is inaccurate or ambiguous, clarify the active replacement rather than rewriting history.
