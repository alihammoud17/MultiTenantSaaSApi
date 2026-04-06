# Entitlements Model (V3)

_Last updated: April 6, 2026._

This document defines the **entitlements model** for V3 so implementation can proceed with clear ownership, tenant safety, and predictable lifecycle behavior.

> Scope note: this is a model/design checkpoint. It does **not** claim that full entitlements enforcement is already implemented in runtime code.

## Objectives

- Extend plan-based access control beyond API request quotas.
- Keep entitlement decisions tenant-scoped and derived from internal subscription state owned by the .NET API.
- Support future add-ons without coupling feature gates to provider-specific payloads.
- Keep evaluation deterministic, auditable, and safe-by-default.

## Ownership and boundaries

### .NET API (system of record)

Owns:
- entitlement definitions used by platform features
- plan-to-entitlement assignments
- tenant-effective entitlement state derived from active subscription lifecycle
- enforcement decisions for tenant-facing/api-facing features

### BillingService

Owns:
- provider-side billing integration and webhook normalization
- provider events that may trigger plan/subscription transitions

Does **not** own:
- direct entitlement enforcement logic
- raw-provider-to-feature mapping rules

## Conceptual model

## 1) EntitlementDefinition
A platform-defined capability key.

Recommended fields:
- `key` (stable unique identifier, e.g., `features.audit_export`)
- `name` (human-readable)
- `description`
- `valueType` (`boolean`, `integer`, `string`)
- `isActive`
- `createdUtc` / `updatedUtc`

## 2) PlanEntitlementAssignment
Maps a plan to entitlement values.

Recommended fields:
- `planId`
- `entitlementKey`
- `value`
- `source` (`PlanDefault`)
- `createdUtc` / `updatedUtc`

## 3) TenantEntitlementOverride (optional, controlled use)
Supports explicit exceptions or future add-ons while preserving deterministic precedence.

Recommended fields:
- `tenantId`
- `entitlementKey`
- `value`
- `source` (`AdminOverride`, `AddOn`, `SupportGrant`)
- `effectiveFromUtc`
- `effectiveToUtc` (nullable)
- `createdByUserId`
- `reason`

## 4) EffectiveEntitlement (computed)
Runtime evaluation result for a tenant.

Recommended output shape:
- `tenantId`
- `entitlementKey`
- `effectiveValue`
- `resolvedFrom` (`Override` | `Plan` | `DefaultDeny`)
- `evaluatedAtUtc`
- `correlationId` (when available)

## Evaluation rules (deterministic precedence)

For each entitlement key requested for tenant `T`:

1. Verify tenant context and active tenant status.
2. Resolve tenant subscription state from the .NET system of record.
3. Determine base value from current plan assignment.
4. Apply valid tenant override/add-on value if present and effective.
5. If unresolved, return **deny/zero** (fail closed).

### Required behaviors

- **Fail closed**: unknown keys or missing mappings must deny.
- **Tenant isolation**: evaluation never uses another tenant's plan/override rows.
- **Lifecycle-aware**: canceled/expired/grace behavior follows subscription state policy.
- **Idempotent read path**: repeated checks with same state produce same result.

## Subscription lifecycle interaction

Entitlements must be derived from current internal subscription lifecycle state:

- `ACTIVE`: plan entitlements apply.
- `GRACE_PERIOD`: apply documented policy per key (default: continue current plan access unless policy says otherwise).
- `CANCELED`: maintain behavior until end-of-period policy boundary.
- `EXPIRED`: deny all non-free/default entitlements.

Lifecycle transition rules should be explicit in implementation and test fixtures to avoid ambiguous access windows.

## Security and multi-tenant safety requirements

- Never trust external provider payload identifiers directly for entitlement assignment.
- Every update path to entitlement-affecting state must validate tenant mapping internally.
- Authorization required for any admin/support override mutation.
- Audit-log every entitlement-affecting mutation with actor, tenant, key, old/new value, reason, and timestamp.
- Do not log secrets/tokens in entitlement evaluation or mutation logs.

## Observability requirements

At minimum, log structured events for:
- entitlement evaluation denials (with non-sensitive reason code)
- lifecycle-driven entitlement changes
- override grants/revocations
- unknown-key or missing-mapping fallback-to-deny events

Recommended dimensions:
- `tenantId`
- `entitlementKey`
- `subscriptionStatus`
- `decision` (`allow`/`deny`)
- `reasonCode`
- `correlationId`

## Rollout plan (implementation sequence)

1. **Persistence foundation**
   - Add entitlement definition + plan mapping schema.
   - Add clear migration with explicit naming.
2. **Evaluation service**
   - Add application-layer evaluator + tests.
3. **Initial feature gates**
   - Gate a small number of existing plan-aware surfaces.
4. **Overrides/add-ons (optional expansion)**
   - Introduce controlled tenant overrides with auditability.
5. **Operational hardening**
   - Add metrics/logs/runbook guidance for denial diagnostics.

## Testing expectations for implementation

When implementation starts, include tests for:
- tenant isolation in entitlement reads/evaluations
- fail-closed behavior for unknown/missing keys
- plan transition and lifecycle transition entitlement changes
- authorization + audit coverage for override mutations
- denial/allow behavior on gated endpoints/services

## Setup and migration notes for this documentation iteration

- This document introduces the **model and execution contract only**.
- No entitlement schema migration is included in this docs update.
- Continue to apply existing migrations as documented in root `README.md`.
