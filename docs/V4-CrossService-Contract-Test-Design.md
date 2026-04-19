# V4 P0 Cross-Service Billing Contract Test Suite Design

Date: April 18, 2026
Status: Implemented baseline (P0 slice completed April 18, 2026; updated April 19, 2026)

## Scope

Design a P0 automated contract test suite for BillingService -> .NET internal billing callback behavior covering:

- signed callback request requirements
- contract version rules
- required payload field expectations
- explicit valid + invalid scenarios

The core P0 suite described in this document is now implemented in repository tests. This document remains as a design+traceability reference for what was delivered and what follow-up coverage can still be added in later slices.

## Contract surfaces to test

### 1) Payload shape contract surface (producer + consumer)

- BillingService producer payload type and mapping:
  - `BillingService/src/shared/types.ts` (`BillingCallbackPayload`)
  - `BillingService/src/jobs/subscriptionSyncJob.ts` (`toCallbackPayload`)
- .NET consumer DTO + required field validation:
  - `Domain/DTOs/BillingCallbackRequest.cs`
  - `Application/Services/BillingCallbackProcessor.cs` (`ValidateRequest`)

### 2) Signed request contract surface

- .NET callback auth header contract:
  - required headers `X-Billing-Timestamp`, `X-Billing-Signature`
  - HMAC payload format `${timestamp}\n${rawJson}`
- Runtime validator implementation:
  - `Application/Services/InternalRequestSignatureValidator.cs`
  - `Presentation/Controllers/InternalBillingController.cs`

### 3) Contract version gate surface

- Accepted version set in .NET (`2026-03-18` only today):
  - `Application/Services/BillingCallbackProcessor.cs` (`SupportedContractVersions`)
- BillingService producer version literal:
  - `BillingService/src/shared/types.ts`
  - `BillingService/src/jobs/subscriptionSyncJob.ts`

### 4) Endpoint wiring surface

- Callback route and handler behavior:
  - `POST /api/internal/billing/subscription-events`
  - `Presentation/Controllers/InternalBillingController.cs`
- Existing integration security helpers and conventions:
  - `Tests/Integration/SecurityTestHelpers.cs`
  - `Tests/Integration/InternalBillingSecurityTests.cs`

## Implemented files/projects (P0 delivered)

### .NET test project (`Tests`)

1. Add new integration test file:
   - `Tests/Integration/CrossServiceBillingContractTests.cs`
2. Reuse/extend helper utilities as needed:
   - `Tests/Integration/SecurityTestHelpers.cs`

### BillingService test project (`BillingService/tests`)

1. Add new contract producer tests:
   - `BillingService/tests/billingCallbackContract.test.ts`
2. (Optional if needed for clean exports) small non-behavioral testability adjustment:
   - `BillingService/src/jobs/subscriptionSyncJob.ts` (export helper or cover via `enqueue(...).payload`)

### Documentation updates

1. Update backlog progress text (design and implementation pointer):
   - `docs/V4-Implementation-Backlog.md`
2. Clarify normative contract details (if accepted):
   - `docs/Internal-Billing-Contract.md`

## Minimum useful P0 test matrix

### A. Positive conformance

1. **Valid signed callback accepted**
   - Proper timestamp + signature + all required fields + supported version
   - Expect `200 OK` and non-duplicate processing

2. **Valid payload from BillingService mapper**
   - `SubscriptionSyncJob` produced payload contains expected field names/types/version
   - Ensures producer shape matches consumer DTO expectations

3. **Required targetPlanId event rules**
   - `subscription.plan_changed` with `targetPlanId` present -> accepted
   - `subscription.downgrade_scheduled` with `targetPlanId` present -> accepted

### B. Signature/auth invalid cases

4. Missing timestamp header -> `401`
5. Missing signature header -> `401`
6. Invalid signature value -> `401`
7. Tampered body after signature generation -> `401`
8. Timestamp outside allowed skew window -> `401`
9. Non-ISO/unparseable timestamp header -> `401`
10. Shared-secret mismatch scenario -> `401` (if test harness supports alt secret)

### C. Contract version invalid cases

11. Unknown/older `contractVersion` -> `400`
12. Missing `contractVersion` -> `400`
13. Wrong type (`contractVersion` numeric/object) -> `400`

### D. Required field validation invalid cases

14. Missing `eventId` -> `400`
15. Missing `eventType` -> `400`
16. Missing `provider` -> `400`
17. Missing `providerEventId` -> `400`
18. Missing `correlationId` -> `400`
19. Empty GUID tenantId -> `400`
20. Empty GUID subscriptionId -> `400`
21. Missing `occurredAtUtc` -> should be explicitly asserted/documented (current behavior likely default `DateTime` acceptance)

### E. Event-specific required field rules

22. `subscription.plan_changed` without `targetPlanId` -> `400`
23. `subscription.downgrade_scheduled` without `targetPlanId` -> `400`
24. Non-plan-change event with absent `targetPlanId` -> accepted

### F. Backward/forward compatibility guardrails

25. Extra unknown JSON fields present -> accepted (unless contract chooses strict rejection)
26. Field casing mismatch (e.g., `eventID`) -> rejected as missing required semantic field

## Docs needing clarification before/with implementation

1. **Timestamp format strictness**
   - Contract currently says header required and skew-validated, but not whether only ISO-8601 round-trip (`O`) is allowed.

2. **`occurredAtUtc` requiredness semantics**
   - Contract marks it required, but .NET DTO currently allows deserialization default values unless explicitly guarded.

3. **Unknown/extra field policy**
   - Confirm whether the endpoint should tolerate unknown additive fields (recommended for forward compatibility).

4. **Version negotiation policy**
   - Document whether multiple versions can be accepted concurrently and for how long during version rollouts.

5. **Provider value constraints**
   - Contract lists examples, but should explicitly define allowed provider enumeration and rejection behavior.

6. **Error contract consistency**
   - Security/validation failures currently return generic error strings; document whether error body shape is stable and testable.

## Remaining follow-up matrix (post-P0 expansions)

The following expansions remain candidates for later V4 slices:

- broaden provider-value constraint checks and unknown-field compatibility assertions
- add explicit negative coverage for additional malformed timestamp formats and alternate clock-skew boundaries
- add multi-version compatibility tests once additional contract versions are introduced
- add test fixtures sourced from production-like replay traces after deployment environments exist
