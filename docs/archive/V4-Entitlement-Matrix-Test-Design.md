# V4 Entitlement Matrix Test Design (Implemented P1.1 Slice)

_Last updated: April 20, 2026._

## Purpose

Document the entitlement regression matrix harness that is now implemented for the completed P1.1 slice.

This is an **implemented-state** design note (not forward-looking architecture).

## Implemented test harness components

- `Tests/UnitTests/Entitlements/EntitlementMatrixCase.cs`
  - shared matrix case model for entitlement key, source inputs, and expected resolution metadata
- `Tests/UnitTests/Entitlements/EntitlementMatrixFixtureBuilder.cs`
  - deterministic fixture builders for boolean, numeric, and endpoint-gate regression case sets
- `Tests/UnitTests/Entitlements/EntitlementMatrixAssertions.cs`
  - reusable assertions for resolved value, allowed/denied result, and resolved-from provenance
- `Tests/UnitTests/EntitlementEvaluatorTests.cs`
  - matrix-driven unit test methods that seed deterministic data and assert evaluator outcomes

## Implemented regression coverage

### 1) Source precedence and merge semantics

The current harness verifies:

- plan default baseline resolution
- add-on contribution resolution
- override precedence over both plan and add-on values
- integer `Increment` add-on merge behavior and override-after-merge behavior

### 2) Endpoint-gated entitlement key coverage

The matrix includes representative entitlement keys used by current progressive gates:

- billing:
  - `feature.billing.invoices.read`
  - `feature.billing.subscription.manage`
  - `feature.billing.plan.upgrade`
- admin:
  - `feature.admin.users.manage.advanced`
- analytics:
  - `feature.analytics.audit_logs.read`

### 3) Regression-oriented allow/deny outcomes

Implemented cases explicitly include both positive and negative paths:

- lower-plan default deny (no required add-on)
- add-on grant allow from denied baseline
- override allow from denied baseline
- override deny over higher-plan/default allow

### 4) Subscription lifecycle combinations

The matrix currently exercises evaluator behavior for:

- `Active`
- `GracePeriod`
- `Canceled`
- `Expired`

Also included:

- missing-subscription default-deny behavior

## Scope boundary (intentional)

This harness is intentionally scoped to evaluator determinism.

It does **not** replace integration tests for:

- tenant isolation boundary enforcement
- authorization pipeline behavior
- endpoint middleware behavior

Those remain covered by integration security suites.

## Known follow-up beyond this completed slice

Not newly implemented in this iteration:

- explicit `Suspended` entitlement-behavior matrix cases
- additional add-on merge modes beyond current representative coverage
- broader endpoint-key combinatorics across all entitlement-gated surfaces
