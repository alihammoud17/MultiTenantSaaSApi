# Entitlements Model (V3)

_Last updated: April 6, 2026._

This document defines the V3 entitlements design **on top of the current plan/subscription implementation** so implementation can proceed incrementally without breaking existing plan upgrade behavior.

> Scope note: foundation implementation is now started (schema + evaluator/enforcer + first billing invoice gate), but runtime entitlement enforcement is **not** fully rolled out yet.

## Current baseline (what exists today)

The .NET API currently supports:

- plan catalog records with coarse plan attributes (`ApiCallsPerMonth`, `MaxUsers`)
- tenant subscription state with lifecycle fields (`PlanId`, `Status`, scheduled downgrade fields, grace/cancel timestamps)
- direct plan upgrade flow (`POST /api/plans/upgrade`) that updates `Subscription.PlanId`
- plan-based API request rate limiting through `RateLimitService`
- billing lifecycle transitions from internal callback events

This model layers feature-level and quota-level entitlements over that baseline without changing system ownership.

## Design objectives

1. Support **feature-level entitlements** (boolean/flag and typed values), not only request limits.
2. Support **hard quotas** (block) and **soft quotas** (allow with warning/overage marker) where appropriate.
3. Support future **add-ons** as explicit, composable entitlement sources.
4. Preserve existing plan upgrade/downgrade behavior as much as possible.
5. Provide a clear **migration + enforcement rollout strategy**.

## Ownership and boundaries

### .NET API (system of record)

Owns:

- entitlement definitions and policies
- plan-to-entitlement mappings
- tenant add-on assignments / overrides
- effective entitlement evaluation used by platform features
- lifecycle-aware enforcement decisions and audit logs

### BillingService

Owns:

- provider-side add-on purchase and webhook normalization (future)
- normalized events that may trigger add-on assignment state changes

Does **not** own:

- feature gating logic in the API
- direct mutation of unrelated entitlement policy

## Domain model (additive)

## 1) `EntitlementDefinition`

A globally defined capability or quota key.

Suggested fields:

- `Key` (stable unique string, e.g., `feature.audit.export`, `quota.projects.count`)
- `DisplayName`
- `Description`
- `ValueType` (`Boolean`, `Integer`, `Decimal`, `String`, `Json`)
- `Category` (`Feature`, `Quota`, `LimitBehavior`, `Operational`)
- `IsActive`
- `DefaultValue` (optional; if absent, deny/zero)
- `CreatedUtc`, `UpdatedUtc`

## 2) `PlanEntitlement`

Baseline values per plan.

Suggested fields:

- `PlanId` (FK to existing plan)
- `EntitlementKey` (FK to definition)
- `Value` (stored normalized as text/json)
- `Source = PlanDefault`
- `CreatedUtc`, `UpdatedUtc`

Unique key: (`PlanId`, `EntitlementKey`).

## 3) `AddOnDefinition` (future-ready now)

Catalog item for purchasable entitlement packs.

Suggested fields:

- `Id` (e.g., `addon.extra-projects-100`)
- `DisplayName`
- `Description`
- `IsActive`
- `BillingProviderProductRef` (optional; provider-neutral wrapper, not raw payload)
- `CreatedUtc`, `UpdatedUtc`

## 4) `AddOnEntitlement`

Maps an add-on to one or more entitlement deltas/values.

Suggested fields:

- `AddOnId`
- `EntitlementKey`
- `ValueMode` (`Set`, `Increment`, `Max`, `Min`)
- `Value`

## 5) `TenantAddOnAssignment`

Tenant-scoped assignment lifecycle (active/scheduled/expired/canceled).

Suggested fields:

- `TenantId`
- `AddOnId`
- `Status`
- `EffectiveFromUtc`
- `EffectiveToUtc` (nullable)
- `ExternalReference` (optional correlation/idempotency marker)
- `CreatedUtc`, `UpdatedUtc`

## 6) `TenantEntitlementOverride` (controlled/admin support)

Exceptional override path, heavily audited.

Suggested fields:

- `TenantId`
- `EntitlementKey`
- `Value`
- `Reason`
- `Source` (`SupportGrant`, `Compensation`, `ManualCorrection`)
- `EffectiveFromUtc`
- `EffectiveToUtc`
- `CreatedByUserId`
- `CreatedUtc`

## Effective entitlement resolution

`EffectiveEntitlement` is computed (not canonical storage), returned by evaluator with provenance:

- `TenantId`
- `EntitlementKey`
- `Value`
- `ResolvedFrom` (`Override`, `AddOn`, `Plan`, `DefaultDeny`)
- `SubscriptionStatus`
- `EvaluatedAtUtc`
- `CorrelationId`

### Deterministic precedence

For tenant `T` and entitlement key `K`:

1. Validate tenant context and active tenant mapping.
2. Resolve subscription + plan from .NET API state.
3. Start with `PlanEntitlement` value for current `PlanId`.
4. Apply active add-on contributions (ordered, deterministic by assignment id + key).
5. Apply active admin override (highest precedence).
6. If key unresolved: fail closed (deny/zero).

## Soft vs hard quota policy model

Quota keys should include companion policy metadata so enforcement is explicit.

Recommended policy dimensions:

- `EnforcementMode`: `Hard`, `Soft`, `Disabled`
- `MeasurementWindow`: `PerRequest`, `Rolling30Days`, `CalendarMonth`, `Concurrent`
- `SoftThresholdPercent` (optional, e.g., 80)
- `ExceededAction`:
  - hard: reject with consistent error code
  - soft: allow + emit warning/metric/audit breadcrumb

Examples:

- `quota.api.calls.monthly` -> existing behavior remains **Hard**.
- `quota.projects.count` -> likely **Hard** for create operations.
- `quota.storage.gb` -> potentially **Soft** during grace window with overage warnings.

## Subscription lifecycle interaction

Entitlements remain lifecycle-aware and consistent with existing subscription semantics:

- `Active`: full plan + add-on + override evaluation.
- `GracePeriod`: preserve access according to per-key lifecycle policy (default: preserve non-destructive feature access, continue quota tracking).
- `Canceled`: maintain access until period boundary unless key explicitly requires immediate removal.
- `Expired`: reduce to free/default entitlements and disable paid add-ons.

Existing scheduled downgrade behavior is preserved by evaluating current `PlanId` until effective boundary, then next `PlanId`.

## Preserving current plan upgrade behavior

To minimize change risk:

- keep `Plans` and `Subscriptions` schema/flow intact
- keep `POST /api/plans/upgrade` contract unchanged initially
- on upgrade, continue writing `Subscription.PlanId` exactly as today
- entitlement evaluator reads that `PlanId` and resolves plan entitlements
- no provider-specific entitlement decisions in this API path

This ensures current tests and tenant workflows around plan upgrades stay valid while entitlements are introduced additively.

## Migration strategy (phased)

## Phase 0 - Documentation + key inventory (current)

- finalize key naming conventions and ownership
- map current plan properties into target entitlement keys

## Phase 1 - Additive schema introduction

Add tables only; no runtime gate flips yet:

- `EntitlementDefinitions`
- `PlanEntitlements`
- `AddOnDefinitions`
- `AddOnEntitlements`
- `TenantAddOnAssignments`
- `TenantEntitlementOverrides`

Migration requirements:

- explicit FK/unique constraints
- tenant-scoped indexes for assignment/override lookups
- migration naming that communicates additive/non-breaking intent

## Phase 2 - Seed + backfill

- seed entitlement definitions for existing known limits/features
- backfill per-plan values for `plan-free`, `plan-pro`
- keep legacy plan fields (`ApiCallsPerMonth`, `MaxUsers`) until parity validation is complete

## Phase 3 - Read-path evaluator (dark launch)

- implement evaluator service with deterministic precedence
- expose internal diagnostics endpoint/log comparison for legacy-vs-new decision checks
- no user-facing behavior changes yet

## Phase 4 - Progressive enforcement rollout

- gate low-risk features first (read-only toggles)
- then gate write actions with hard/soft quota policies
- keep existing rate-limit middleware as-is until entitlement-backed quota parity is proven

## Phase 5 - Consolidation

- after parity and soak period, move remaining checks to entitlements
- remove duplicated legacy checks only when coverage + observability are sufficient

## Enforcement strategy

Use a single application-layer abstraction:

- `IEntitlementEvaluator` -> returns effective values + source
- `IEntitlementEnforcer` -> evaluates policy (`allow`, `allow_with_warning`, `deny`)

Integration points:

- controllers for action-level feature gates
- service layer for business-operation gates
- middleware only for cross-cutting quotas with consistent headers/metadata

Response semantics:

- hard denial: consistent error code/body (`EntitlementDenied` / `QuotaExceeded`)
- soft exceedance: success response plus warning metadata/header where applicable

## Security and tenant safety

- never resolve entitlement state without validated tenant context
- never trust provider payload ids directly for tenant add-on assignment
- require explicit permission for override mutations
- audit every entitlement-affecting mutation (old/new/source/reason/actor/tenant)
- keep logs structured and secret-safe

## Testing strategy (when implementation begins)

Required automated coverage:

- tenant isolation in all evaluator queries
- unknown key + missing mapping fail-closed behavior
- upgrade/downgrade and lifecycle transition effects on effective entitlements
- hard-vs-soft quota outcomes on gated endpoints
- add-on assignment idempotency and expiration behavior
- authorization + audit checks for override endpoints

## Risks and mitigations

- **Risk:** policy drift between legacy checks and new evaluator during rollout.
  - **Mitigation:** dark-launch comparison metrics + staged gate enablement.
- **Risk:** ambiguous add-on stacking behavior.
  - **Mitigation:** explicit `ValueMode` and deterministic ordering rules.
- **Risk:** accidental cross-tenant reads in entitlement joins.
  - **Mitigation:** tenant-scoped query predicates + isolation tests for every new read path.

## Implementation readiness checklist

Before coding starts for this slice:

- key catalog reviewed by API + billing owners
- first gated endpoints selected
- migration names + rollback plan drafted
- telemetry fields for deny/warn decisions agreed
