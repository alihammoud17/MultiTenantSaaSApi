# V4 Technical Debt Planning (Planning-Only)

> Date: April 29, 2026  
> Scope: planning artifact only (no implementation)  
> Guardrails: preserve existing HTTP contracts/response shapes; keep tenant isolation and ownership boundaries intact.

## Summary Table

| Item | Priority | Effort Estimate | Depends On |
| --- | --- | --- | --- |
| 4) Missing unit tests for `IdentityLifecycleService` and `MfaService` | P0 | Small (1-2 slices) | None |
| 3) Missing CORS configuration in `Program.cs` | P0 | Small (1 slice) | None |
| 2) Per-request DB query redundancy between tenant resolution and rate-limiting | P1 | Medium (2 slices) | 4 (recommended, not required) |
| 5) Entitlement evaluator query batching | P1 | Medium (2 slices) | 4 (recommended, not required) |
| 1) Domain folder typo rename (completed) | P2 | Medium-Large (2-3 slices) | 4 (recommended), 2 and 5 should be stable first |
| 6) Outbound webhook endpoint management surface and signing secret rotation | P0 (security) / P2 (scope size) | Large (3-4 slices) | 4 (recommended), after 2/5 for cleaner service-layer reuse |

---

## 4) Missing Unit Tests for `IdentityLifecycleService` and `MfaService`

### Problem and why it matters
`IdentityLifecycleService` and `MfaService` contain security-sensitive logic (invite acceptance, verification, password reset, OTP verification, opaque token hashing) but currently lack dedicated unit-test suites comparable to other core services. This increases regression risk for tenant-safe identity behavior and authentication hardening, especially as V4 continues to refactor controller/service boundaries.

### Smallest safe thin vertical slice
Add focused unit tests for deterministic core behaviors only: success/deny paths, token validity windows, tenant scoping for identity records, and MFA code format/verification behavior. Keep tests service-level and avoid any response-contract changes.

### Files likely to be created or modified
- `Tests/UnitTests/IdentityLifecycleServiceTests.cs` (new)
- `Tests/UnitTests/MfaServiceTests.cs` (new)
- `Tests/Tests.csproj` (if includes need adjustment)
- Optional shared test helpers under `Tests/UnitTests/...` if needed minimally

### Test coverage expectations
- Tenant isolation checks for invite/verification/reset token lookups (same-tenant vs cross-tenant behavior)
- Expired/used token rejection cases
- Duplicate user/invite edge cases
- MFA: invalid format rejection, valid-code acceptance within allowed drift window, token/hash format checks

### Migration, documentation, or runbook requirements
- No DB migration expected
- Update `docs/V4-Implementation-Backlog.md` when executed
- Update `README.md` only if test command workflow changes (not expected)

### Dependencies or ordering constraints
No hard dependency. Recommended first because it reduces risk for all subsequent refactors and security-sensitive slices.

### Risks and assumptions
- **Risk:** time-dependent MFA tests can be flaky if clock handling is not controlled.
- **Assumption:** tests can remain deterministic with narrow assertions and current time-window tolerance patterns.

---

## 3) Missing CORS Configuration in `Program.cs`

### Problem and why it matters
The API host currently lacks explicit CORS policy registration/middleware wiring. In local pre-deployment usage this can block browser-based clients, and in later environments it can create ad hoc/proxy workarounds that bypass explicit security intent. CORS must be explicit, environment-configurable, and non-breaking for current API contracts.

### Smallest safe thin vertical slice
Introduce a minimal named CORS policy sourced from configuration (`AllowedOrigins` style list), register with `AddCors`, and apply with `UseCors` at the correct pipeline position. Start with least-permissive defaults and preserve existing headers/body response shapes.

### Files likely to be created or modified
- `Presentation/Program.cs`
- `Presentation/appsettings*.json` (or existing configuration files holding API settings)
- `README.md` (configuration key documentation)
- `docs/V4-Implementation-Backlog.md`

### Test coverage expectations
- Integration coverage for expected preflight behavior on representative endpoint(s)
- Negative test for disallowed origin behavior
- No changes to existing auth/tenant error response shapes

### Migration, documentation, or runbook requirements
- No migration
- Document new config keys and local defaults in `README.md`
- If smoke should verify CORS in-browser assumptions, update runbook text only (no broad smoke redesign)

### Dependencies or ordering constraints
Independent. Can ship early after tests to reduce frontend integration friction.

### Risks and assumptions
- **Risk:** overly broad wildcard setup can weaken security posture.
- **Assumption:** existing clients can provide explicit origins, allowing a constrained policy.

---

## 2) Per-request DB Query Redundancy Across Tenant Resolution and Rate-Limiting *(Implemented May 1, 2026)*

### Problem and why it matters
Current request flow resolves tenant in `TenantMiddleware`, then `RateLimitService` re-queries subscription/plan data per request. This duplicates database work, increases latency, and adds avoidable load on high-traffic tenant-authenticated paths. It also creates multiple places where tenant context interpretation can diverge over time.

### Implemented slice summary
Added scoped request-level reuse for tenant access data: `RateLimitMiddleware` now preloads the tenant plan API-call limit into `IRequestTenantAccessContext`, and `RateLimitService` consumes that scoped value before falling back to the existing subscription+plan query path when preload is unavailable.

### Files updated in this slice
- `Presentation/Middleware/RateLimitMiddleware.cs`
- `Application/Services/RateLimitService.cs`
- scoped request access context interface/model + DI wiring used by middleware/service handoff

### Validation/status
- Rate-limit behavior parity is preserved by keeping fallback query semantics when scoped preload is unavailable.
- Tenant middleware ownership and rejection behavior remain unchanged.
- `README.md` and `docs/V4-Implementation-Backlog.md` now document the request-scoped reuse pattern.

### Migration/documentation status
- No migration required.
- Documentation updated in `docs/V4-Implementation-Backlog.md` and `README.md`.

### Dependencies or ordering constraints
Best after item 4 test gap closure. Should precede larger endpoint-surface additions (item 6) so new surfaces can reuse stable per-request context patterns.

### Risks and assumptions
- **Risk:** caching tenant-derived plan data incorrectly could produce stale/incorrect rate-limit decisions.
- **Assumption:** per-request scope is sufficient and does not require cross-request caching semantics.

---

## 5) Entitlement Evaluator Query Batching

### Problem and why it matters
`EntitlementEvaluator` currently executes multiple sequential queries (subscription, definition, plan entitlement, add-on contributions, overrides) for each evaluation call. This can amplify query count under endpoint-gating and increase latency, especially when multiple entitlements are checked in one request lifecycle.

### Smallest safe thin vertical slice
Batch evaluator reads into fewer queries for the single-entitlement path first (without changing evaluator outputs). Preserve precedence order (default -> plan -> add-on merge -> override) and existing status/source semantics.

### Files likely to be created or modified
- `Application/Services/EntitlementEvaluator.cs`
- `Tests/UnitTests/EntitlementEvaluatorTests.cs`
- Potential new internal projection models/helpers near evaluator

### Test coverage expectations
- Regression suite must continue asserting precedence and lifecycle-state semantics
- Add coverage to verify no behavioral changes for null/default/override edge cases
- Optional targeted performance-oriented test (query count guard) if test infrastructure supports it safely

### Migration, documentation, or runbook requirements
- No migration expected
- Update `docs/V4-Implementation-Backlog.md` with scope and invariants preserved
- No runbook changes expected

### Dependencies or ordering constraints
Can run independently but should be sequenced near item 2 because both reduce request-path DB pressure and may share context patterns.

### Risks and assumptions
- **Risk:** batching refactor may accidentally alter precedence or `resolvedFrom` attribution.
- **Assumption:** current unit matrix coverage is strong enough to detect semantic drift once expanded.

---

## 1) Domain Folder Typo Rename (Completed)

### Problem and why it matters
The domain folder typo hurt developer experience, search/discovery consistency, and long-term maintainability. The rename is now completed as a mechanical cleanup.

### Smallest safe thin vertical slice
Execute rename as a pure structural move with no behavioral code changes: keep files under `Domain/Entities`, update namespace/usings/project file references as required, and run full build/test validation. Avoid piggybacking unrelated refactors.

### Files likely to be created or modified
- Entire domain entity set resides under `Domain/Entities/*`
- Referencing files across `Application/`, `Infrastructure/`, `Presentation/`, and `Tests/`
- `Domain/Domain.csproj` and/or solution/project include metadata if path-based includes are explicit
- Docs mentioning paths (if any)

### Test coverage expectations
- Full regression pass (unit + integration)
- No new behavior tests required; this is a no-behavior-change safety migration
- Ensure CI/path tooling remains stable

### Migration, documentation, or runbook requirements
- No schema migration
- Minimal documentation updates where path references are user-facing
- Add explicit note in backlog/doc that this is a mechanical rename only

### Dependencies or ordering constraints
Should be delayed until higher risk-reduction items are complete to avoid merge conflict churn across active V4 slices.

### Risks and assumptions
- **Risk:** broad file moves create noisy diffs and hidden broken references.
- **Assumption:** namespaces can remain stable even if folder path changes, minimizing runtime risk.

---

## 6) Outbound Webhook Endpoint Management Surface + Signing Secret Rotation

### Problem and why it matters
Outbound webhook delivery exists, but management capabilities for endpoint lifecycle and signing-secret rotation are not yet a complete tenant-safe operational surface. Without explicit management APIs/workflows, secret hygiene, compromise response, and endpoint governance are weaker than desired for pre-deployment maturity.

### Smallest safe thin vertical slice
First slice: add a tenant-scoped read/list + rotate-secret action for existing endpoint records only (no broad CRUD expansion initially). Require authenticated tenant context and RBAC permission checks; rotate by generating a new secret version, storing only hashed/secured representation per existing conventions, and preserving delivery contract compatibility.

### Files likely to be created or modified
- Likely new/expanded controller under `Presentation/Controllers/` for tenant webhook endpoint management
- Service-layer logic in `Application/Services/` (endpoint management + rotation orchestration)
- Domain DTOs/contracts in `Domain/DTOs/` and interfaces in `Domain/Interfaces/`
- Persistence model/config updates for secret versioning metadata in `Domain/Entities` + `Infrastructure/Data` mappings/migrations (if required)
- Existing outbound signer/publisher components if key-resolution behavior changes
- `docs/Outbound-Webhooks.md`, `docs/Outbound-Webhook-Contract.md`, `README.md`, `docs/V4-Implementation-Backlog.md`

### Test coverage expectations
- **Tenant isolation (explicit):** tenant A cannot read, rotate, disable, or otherwise mutate tenant B webhook endpoints under any identifier tampering path
- Authorization tests for sensitive management actions
- Rotation behavior tests: old-secret invalidation policy, new-secret effectiveness, replay/idempotency expectations unchanged
- Negative-path tests for invalid rotation requests and audit/observability assertions

### Migration, documentation, or runbook requirements
- Migration likely if storing secret version, rotated-at timestamps, or status flags
- Update operational docs/runbooks for rotation process, rollback/recovery guidance, and safe logging requirements
- Document any new env/config keys; never log raw secrets

### Dependencies or ordering constraints
High security value, but scope is larger. Recommended after item 4 (tests) and after query-efficiency items 2/5 if shared context/query improvements are desired before expanding surface area.

### Risks and assumptions
- **Risk:** secret rotation can break deliveries if signer resolution/versioning is inconsistent.
- **Risk:** management endpoints create new cross-tenant attack surface if scoping/authorization is incomplete.
- **Assumption:** current outbound model can be extended additively without breaking existing delivery contract/response shapes.

---

## Recommended Execution Order (Updated May 1, 2026)

1. **Item 4 — Missing unit tests (`IdentityLifecycleService`, `MfaService`)**: Highest immediate risk reduction for security-sensitive logic with low effort.
2. **Item 3 — CORS configuration**: Small, high-practicality hardening slice that unblocks browser clients and keeps policy explicit.
3. **Item 5 — Entitlement evaluator query batching**: Next request-path efficiency improvement with strict semantic-regression protection.
4. **Item 6 — Outbound webhook management + secret rotation**: High security value but larger blast radius; safer after test and request-path foundations are strengthened.
5. **Item 1 — domain typo rename (completed)**: DX cleanup delivered as a mechanical rename; keep future slices free of folder/name churn piggybacks.
6. **Item 2 — Tenant/rate-limit per-request query redundancy (completed May 1, 2026)**: Scoped preload + fallback query reuse now in place.

## First Recommended Slice to Execute

Start with **Item 4** by adding unit-test suites for `IdentityLifecycleService` and `MfaService`.

One-slice objective: establish deterministic coverage for tenant-scoped identity token flows and MFA verification/token hashing behavior, with no production-code behavior changes and no contract changes.
