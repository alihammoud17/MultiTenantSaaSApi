# Status: V4 tech-debt plan and its follow-ups

- **Date:** 2026-10-24
- **Source:** [`docs/archive/V4-TechDebt-Planning.md`](../archive/V4-TechDebt-Planning.md) (archived; left unchanged as history)
- **Goal:** Record what that plan delivered, finish its remaining entitlement follow-ups, and list what is still open.
- **Status:** entitlement follow-ups done; outbound-webhook follow-ups open.

## The six planned items

All six were already delivered before this pass. Checked against the code on `master` (`38ee745`):

| Item | Status | Where it lives now |
| --- | --- | --- |
| 1. `Domain/Entites` → `Domain/Entities` rename | Done | `Domain/Entities/` |
| 2. Tenant-resolution / rate-limit query reuse | Done | `IRequestTenantAccessContext`, preloaded in `Presentation/Middleware/RateLimitMiddleware.cs`, read by `Application/Services/RateLimitService.cs` |
| 3. Explicit CORS policy | Done, then superseded | The permissive `InitialExplicitCorsPolicy` was replaced by the configured allowlist and per-tenant origins (`Presentation/Cors/`); see [Operations](../operations.md#cors) |
| 4. Identity lifecycle + MFA unit tests | Done | `Tests/UnitTests/IdentityLifecycleServiceTests.cs`, `MfaServiceTests.cs` |
| 5. Entitlement evaluator query batching | Done | `Application/Services/EntitlementEvaluator.cs` |
| 6. Outbound-webhook endpoint management + rotation initiation | Done (phase 1) | `WebhookEndpointsController`, `WebhookEndpointManagementService` |

The archived plan's summary table still lists items 2 and 3 as P1 and P0, but their own sections say they were implemented. The code confirms the sections.

## Follow-ups finished in this pass (item 5)

The plan left three entitlement follow-ups open. Each landed as its own commit with the full test suite green:

1. **Query-count harness.** `Tests/UnitTests/Entitlements/QueryCountingInterceptor.cs` counts the commands a `DbContext` sends. `Tests/UnitTests/EntitlementEvaluatorQueryCountTests.cs` pins the cost of evaluation: 3 queries when plan, add-on and override all contribute, 1 when the tenant has no subscription.
2. **Multi-key batching.** `IEntitlementEvaluator.EvaluateManyAsync` resolves N keys with the same 3 queries as one key. `EvaluateAsync` is now a batch of one, so every existing matrix and endpoint test covers the batch path. Checked on PostgreSQL 16 as well as SQLite.
3. **Request-scope memoization.** Within one HTTP request, each `(tenant, key)` is resolved once and reused. With no `HttpContext` (background scopes, plain unit tests) nothing is memoized.

No HTTP contract changed: evaluation results are never serialized, and the gated endpoints return the same status codes and bodies.

## Still open

From item 6 (outbound webhooks). These change what tenants verify, so each needs its own design and contract review:

- Promote `NextSigningSecret` to the active signing secret, finishing a rotation.
- Overlap and rollback policy during a staged cutover (for example, signing with both secrets for a window).
- Operator automation for scheduled and incident-driven rotations.
- Tenant-facing replay and delivery-diagnostic workflows.

From item 5, now unblocked but not needed yet:

- No endpoint gates on more than one entitlement today, so nothing calls `EvaluateManyAsync` in production. Use it when an endpoint or a tenant entitlement summary needs several keys.
- Memoized results are not invalidated within a request. If an action ever changes a tenant's plan, add-ons or overrides and then re-checks an entitlement in the same request, it must evaluate in a fresh scope or the memo needs an invalidation hook.
