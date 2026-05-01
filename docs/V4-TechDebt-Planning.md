# V4 Technical Debt Planning (Planning-Only)

> Date: April 29, 2026  
> Scope: planning artifact only (no implementation)  
> Guardrails: preserve existing HTTP contracts/response shapes; keep tenant isolation and ownership boundaries intact.

## Summary Table

| Item | Priority | Effort Estimate | Depends On |
| --- | --- | --- | --- |
| 4) Identity lifecycle + MFA unit-test gap (completed May 1, 2026) | Completed | Delivered | None |
| 3) Missing CORS configuration in `Program.cs` | P0 | Small (1 slice) | None |
| 2) Per-request DB query redundancy between tenant resolution and rate-limiting | P1 | Medium (2 slices) | 4 (recommended, not required) |
| 5) Entitlement evaluator query batching (completed May 1, 2026) | Completed | Delivered | 4 (recommended, not required) |
| 1) Domain folder typo rename (completed) | P2 | Medium-Large (2-3 slices) | 4 (recommended), 2 and 5 should be stable first |
| 6) Outbound webhook endpoint management surface and signing secret rotation | P0 (security) / P2 (scope size) | Large (3-4 slices) | 4 (recommended), after 2/5 for cleaner service-layer reuse |

---

## 4) Identity lifecycle + MFA unit-test gap *(Completed May 1, 2026)*

### Implemented coverage now in place
- `Tests/UnitTests/IdentityLifecycleServiceTests.cs`
  - invite field normalization + notification emission
  - tenant-safe invite acceptance guard (cross-tenant token use rejected)
  - verification token one-time use (replay rejection)
  - password-reset token one-time use + password hash rotation
- `Tests/UnitTests/MfaServiceTests.cs`
  - enrollment secret/provisioning URI shape
  - TOTP acceptance across configured drift window
  - malformed/null input rejection and invalid base32 error behavior
  - opaque token entropy/format and deterministic SHA-256 hashing

### Intentionally integration-tested only (remaining)
- end-to-end HTTP contract/response-shape behavior for auth endpoints
- middleware/policy interactions (tenant resolution, authz, rate limiting)
- cross-service billing callback and contract conformance behavior

### Notes
- No schema migration required.
- Scope stayed additive: tests + documentation only.

---

## 3) CORS Baseline Explicit in `Program.cs` *(Implemented May 1, 2026)*

### Problem and why it mattered
The API host previously lacked explicit CORS registration/middleware wiring. That made browser integration behavior less deterministic for local pre-deployment validation.

### Implemented slice summary
The API now registers and applies a named CORS policy: `InitialExplicitCorsPolicy`. The current effective policy is explicit and intentionally permissive for V4 local/pre-deployment iteration:
- `AllowAnyOrigin`
- `AllowAnyHeader`
- `AllowAnyMethod`

### Files updated in this slice
- `Presentation/Program.cs`
- `README.md`
- `docs/V4-Implementation-Backlog.md`

### Validation/status
- Named policy wiring is explicit in code and middleware order.
- No response-shape/auth/tenant behavior changes were introduced by this slice.
- Policy tightening for browser clients is deferred to a follow-up hardening slice before production deployment.

### Migration/documentation status
- No migration required.
- Documentation updated in `README.md` and `docs/V4-Implementation-Backlog.md`.

### Dependencies or ordering constraints
Completed as an independent low-blast-radius baseline.

### Risks and assumptions
- **Risk:** wildcard allowances are not appropriate for production browser exposure.
- **Assumption:** this permissive baseline is temporary and will be tightened with explicit browser-client origin/header/method constraints in a later slice.

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

## 5) Entitlement Evaluator Query Batching *(Completed May 1, 2026)*

### Implemented slice summary
- `EntitlementEvaluator` now reads subscription status + plan entitlement + active override via one subscription projection query for each entitlement evaluation call, reducing sequential evaluator reads while keeping output semantics unchanged.
- Existing add-on contribution merge flow and `resolvedFrom` attribution order remain unchanged (`Default`/`DefaultDeny` -> `Plan` -> `AddOn` -> `Override`).

### Files updated in this slice
- `Application/Services/EntitlementEvaluator.cs`
- `Tests/UnitTests/EntitlementEvaluatorTests.cs`
- `docs/V4-Implementation-Backlog.md`
- `README.md`

### Regression protection added in this finalization step
- Added explicit precedence regression test covering plan + add-on + override composition to ensure override remains final winner when all three sources are present.
- Added explicit default-deny regression test for missing definition and missing source values to prevent semantic drift toward implicit allow.
- Existing matrix cases continue to protect representative endpoint-gated billing/admin/analytics entitlement keys and lifecycle combinations.

### Remaining performance work (intentionally out of scope)
- Multi-entitlement evaluation batching (evaluate N keys with shared tenant/subscription snapshot in one call path).
- Optional query-count instrumentation/assertions in tests when a stable provider-backed query-capture harness is available.
- Potential request-scope memoization for repeated same-key evaluations within one request, provided tenant safety and lifecycle freshness semantics are preserved.

### Migration/documentation status
- No migration required.
- No runbook changes required for this slice.

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

1. **Item 4 — Identity lifecycle + MFA unit-test gap**: Completed May 1, 2026 with dedicated deterministic unit suites.
2. **Item 3 — CORS configuration**: Small, high-practicality hardening slice that unblocks browser clients and keeps policy explicit.
3. **Item 5 — Entitlement evaluator query batching**: Next request-path efficiency improvement with strict semantic-regression protection.
4. **Item 6 — Outbound webhook management + secret rotation**: High security value but larger blast radius; safer after test and request-path foundations are strengthened.
5. **Item 1 — domain typo rename (completed)**: DX cleanup delivered as a mechanical rename; keep future slices free of folder/name churn piggybacks.
6. **Item 2 — Tenant/rate-limit per-request query redundancy (completed May 1, 2026)**: Scoped preload + fallback query reuse now in place.

## First Recommended Slice to Execute

Item 4 is complete; keep remaining auth behavior validation focused on integration and contract suites where HTTP/middleware composition is the primary risk surface.
