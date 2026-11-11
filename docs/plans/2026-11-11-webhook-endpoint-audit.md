# Audit Outbound Webhook Endpoint Changes

- **Date:** found Wed 11 Nov 2026, while trying the MCP read tools (see [the MCP server plan](2026-11-10-mcp-server.md))
- **Repo:** MultiTenantSaaSApi
- **Branch:** `feat/webhook-endpoint-audit` (from `master`, after the MCP branch is merged)
- **Status:** implemented on `feat/webhook-endpoint-audit`; PR not opened yet
- **Focus:** Close an audit gap on a security-relevant tenant surface
- **Time:** ~1.5–2 h

Tutor mode: I type the commands and write the code. Claude explains each step, reviews it (build, tests) and ticks the boxes. Claude writes a step's code only when I say "hand it over".

## Deliverables
- [x] **D1.** Every successful webhook endpoint change writes a tenant audit entry, with no signing secrets in it
- [x] **D2.** An integration test that pins the audit actions and checks that no secret leaks into `Changes`
- [x] **D3.** Docs updated (`docs/operations.md`, `docs/CHANGELOG.md`)

---

## Finding

`list_webhook_endpoints` showed two endpoints created at 09:42:39 and 09:42:45 UTC, but `list_audit_logs` had nothing between the login at 09:42:34 and `CORS_ORIGIN_ADDED` at 09:42:57.

Cause: auditing was never added to this feature. Nothing in the code, docs or git history says it was left out on purpose. Commits `5f69470` and `4653852` (2026-05-01) have empty bodies.

- `Application/Services/WebhookEndpointManagementService.cs` only takes `ApplicationDbContext` and `IWebhookEndpointSecretIssuer`. It never calls `IAuditService`.
- `Presentation/Controllers/WebhookEndpointsController.cs` doesn't call it either.
- **All five changes are unaudited**, not just create:

| Service method | Route (`BillingManage` policy) |
|---|---|
| `CreateEndpointAsync` | `POST /api/v1/tenant/webhook-endpoints` |
| `UpdateEndpointAsync` | `PUT /{endpointId}` |
| `SetEndpointActiveStateAsync` | `PATCH /{endpointId}/status` |
| `RotateSigningSecretAsync` | `POST /{endpointId}/rotate-secret` |
| `DeleteEndpointAsync` | `DELETE /{endpointId}` |

Rotate-secret and delete matter most: today nobody can tell from the audit log who rotated a secret or removed an endpoint, or when.

## Design decisions (read before coding)

1. **Copy the CORS pattern.** `TenantCorsOriginService` injects `IAuditService` and calls `LogAsync(action, nameof(Entity), entity.Id.ToString(), new { ... })` after a successful save (`:85`, `:105`). Do the same in `WebhookEndpointManagementService`, so the controller stays transport-only.
2. **Audit only on success.** Write the entry after `SaveChangesAsync` succeeds. Write nothing when the method returns `null` / `false` (not found) or the save throws (for example, a duplicate name that becomes `409`).
3. **Never log secrets.** `SigningSecret` and `NextSigningSecret` must never be in `Changes`. Build the anonymous object field by field. Never pass the entity or `WebhookEndpointManagementResult`, because the result carries the plaintext secret.
4. **Action names** (inline strings, like the rest of the repo):

| Method | Action | `Changes` |
|---|---|---|
| Create | `WEBHOOK_ENDPOINT_CREATED` | `{ Name, CallbackUrl, SubscribedEventTypes }` |
| Update | `WEBHOOK_ENDPOINT_UPDATED` | `{ Before = { Name, CallbackUrl, SubscribedEventTypes }, After = { ... } }` |
| Status | `WEBHOOK_ENDPOINT_ACTIVATED` / `WEBHOOK_ENDPOINT_DEACTIVATED` | `{ Name }` |
| Rotate | `WEBHOOK_ENDPOINT_SECRET_ROTATED` | `{ Name, NextSigningSecretIssuedAtUtc }` |
| Delete | `WEBHOOK_ENDPOINT_DELETED` | `{ Name, CallbackUrl }` |

   `EntityType` = `nameof(TenantWebhookEndpoint)`, `EntityId` = the endpoint id.

5. **Tenant id.** `AuditService` takes the tenant from `ITenantContext`, not from the service's `tenantId` parameter. In an HTTP request both come from the JWT, so they match. Don't change `AuditService` in this slice.
6. **Not atomic (same as CORS).** `AuditService.LogAsync` saves on its own, so the change and its audit entry are two commits. Keep that, to match the existing pattern. Making them one transaction would be a separate, repo-wide change.

---

## Block 1: Implement (45 min)
- [x] Inject `IAuditService` into `WebhookEndpointManagementService`. It's already registered as scoped in `Presentation/Program.cs:75`, so no DI change is needed.
- [x] Add one `LogAsync` call per method, using the table above.
- [x] Update: capture the "before" values before assigning the new ones.
- [x] Status: choose the action from `isActive`.
- [x] Check that every unit test which builds the service by hand still compiles. Pass a fake or no-op `IAuditService` where needed.

## Block 2: Tests (45 min)
- [x] Integration test in `Tests/Integration/WebhookEndpointManagementTests.cs` (same style as `TenantCorsOriginTests.cs:242`):
  - Create, update, deactivate, activate, rotate-secret and delete one endpoint through the API.
  - Read `AuditLogs` with `IgnoreQueryFilters()`, filtered by tenant and `EntityId`, and assert the 6 actions.
  - Assert that no `Changes` value contains the secret returned by create or rotate.
- [x] Negative case: an update or delete of an unknown id (`404`) and a duplicate-name create (`409`) write no audit entry.
- [x] Tenant isolation: tenant B's entries never show up in tenant A's audit log (`GET /api/v1/tenant/audit-logs` or the `list_audit_logs` MCP tool).

## Block 3: Docs, validation and PR (30 min)
- [x] `docs/operations.md` → "Outbound-webhook operations": list the audited actions. The CORS sentence at `:362` is the model to copy.
- [x] `docs/CHANGELOG.md` → Unreleased: one line, "Webhook endpoint create, update, activate/deactivate, secret rotation and delete are now audited (`WEBHOOK_ENDPOINT_*`); no secrets are logged."
- [x] `dotnet build`, `dotnet test` (or `scripts/dev.sh test`), `git diff --check`. Record the exact results in the PR.

---

## Related findings (same session, not in this slice)

- **CORS audit time is ~6 s after `createdAt`.** `TenantCorsOriginService.AddAsync` sets `CreatedAt`, saves, then awaits `_cache.InvalidateAsync` (a Redis `INCR`, `RedisCorsOriginCache.cs:72`) before writing the audit entry. If Redis was unreachable locally, the Redis client's ~5 s timeout would explain the gap. **Unconfirmed:** look in the API log for `CORS origin cache invalidation failed` around 09:42:57 and 09:43:09 UTC on 2026-11-11. If that's the cause, an option is to write the audit entry before invalidating the cache.
- **Different timestamp field names between MCP tools.** `list_cors_origins` returns `createdAt` (`TenantCorsOriginView`), while `list_webhook_endpoints` returns `createdAtUtc`. Renaming would change the REST response too, so it's a contract decision, not a quick fix.
