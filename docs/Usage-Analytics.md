# Usage Analytics (V3 Foundation)

_Last updated: April 13, 2026_

## Scope and status

This document describes the currently implemented **usage analytics foundation** in the .NET API and the intended follow-up scope for V3.

Current status:

- foundation is implemented and tenant-safe
- analytics are sourced from tenant-scoped audit activity already owned by the .NET API
- this is **not** a full BI/export pipeline yet

## Ownership and boundaries

The .NET API remains the system of record for tenant identity, authorization, audit events, and tenant-scoped operational data. Usage analytics in this iteration are computed inside the .NET API from those internal records.

This iteration does **not** move analytics ownership to `BillingService`, and it does not depend on raw billing-provider payloads.

## Implemented API surface

- `GET /api/v1/tenant/analytics/usage`

Implemented behavior:

- tenant context enforcement is required
- endpoint authorization follows existing tenant/admin authorization patterns
- query lookback is bounded with a safe `days` clamp
- optional action filtering is supported
- top-action aggregation is included for quick tenant usage summaries

## Security and tenant isolation expectations

The analytics slice follows repository tenant-safety rules:

- no cross-tenant reads
- tenant id is resolved and enforced by normal tenant context flow
- header tampering and cross-tenant access paths are rejected
- analytics only aggregate events that already belong to the authenticated tenant scope

## Operational notes

- this foundation is optimized for platform-level operational visibility and product feedback loops, not large historical analytics workloads
- for larger analytics/reporting workloads, add dedicated read models or export paths rather than overloading transactional queries

## Tests and validation focus

The implemented test coverage for this slice should continue to emphasize:

- tenant isolation
- authorization behavior on analytics reads
- safe handling of bounded query windows
- cross-tenant tampering rejection

## Follow-up backlog

Planned follow-up (not completed in this iteration):

1. add richer usage dimensions (for example, entitlement/add-on keyed metrics)
2. add export/read-model strategy for larger analytical workloads
3. align analytics event coverage with outbound webhook expansion for external sinks
