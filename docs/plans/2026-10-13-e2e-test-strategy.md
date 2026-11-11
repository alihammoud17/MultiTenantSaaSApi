# Plan: end-to-end test strategy for the .NET API

- **Date:** 2026-10-13
- **Scope:** the .NET API only. BillingService is out of scope for now.
- **Goal:** Add a small, journey-based E2E suite. It runs the shipped API image against real PostgreSQL and Redis over real HTTP, to cover what the unit and integration tests deliberately replace.
- **Status:** proposed. Not started.
- **Who codes it:** the user, with Claude reviewing each step.

## Why E2E, given the existing tests
The integration tests (`Tests/Integration/ApiWebApplicationFactory.cs`) already run the real middleware, auth, controllers and EF in-process. E2E should not repeat them. It exists for what they replace:

| Replaced in integration tests | Risk it hides |
|---|---|
| SQLite in-memory with `EnsureCreated` | PostgreSQL behaviour, real migrations, unique indexes, case and collation, concurrency |
| `AlwaysAllowRateLimitService` | The plan-aware Redis limiter (F-021) is never really exercised |
| `TestServer`, no network | Kestrel, forwarded headers, `Host`-based tenant resolution in the real image |
| In-memory configuration | The env-var configuration of the image built from `deploy/api.Dockerfile` |
| `CapturingIdentityNotificationService` | How verification, invite and reset tokens reach the user (see the blocker below) |

Integration tests stay responsible for edge cases and error branches. E2E aims for roughly 15–25 tests that walk through whole user journeys.

## Tools
- **xUnit v3 and FluentAssertions**, the same as `Tests`. Use an xUnit v3 assembly fixture (`[assembly: AssemblyFixture(typeof(E2EStack))]`) so the stack starts once per run.
- **Testcontainers for .NET:** `Testcontainers.PostgreSql`, `Testcontainers.Redis`, and the core `Testcontainers` package with `ImageFromDockerfileBuilder` to build `deploy/api.Dockerfile`. Run all containers on one Docker network. We pick this over `docker compose` (`compose.yml` reads env files from `/etc/multitenant-saas-api/`) so the tests own the stack's lifecycle, the same way locally and in CI.
- **WireMock.Net**, added later, as the receiver for outbound webhooks (F-016, F-017). It runs as a container on the same network, and tests assert on the requests it received.
- **A plain `HttpClient`** with a small hand-written `ApiClient` wrapper. No Refit or Playwright, since there is no UI.
- **No Respawn and no DB resets.** Each test creates its own tenant with a GUID subdomain, so tests are isolated, can run in parallel and need no cleanup.

## Structure
- Add a new **`Tests.E2E`** project to `MultiTenantSaaSApi.sln`, separate from `Tests`, so running `Tests` still needs no Docker. Run the suite explicitly with `dotnet test Tests.E2E`.
- Reference `Domain` for DTOs. Reference `Infrastructure` only for the fixture's arrange steps (migrations, and seeding a verified tenant; see the blocker below). Never reference `Presentation`, so the suite stays black-box towards the API.
- **`E2E_BASE_URL` override:** when it is set, the fixture starts no containers and targets that URL. The same suite then serves as a post-deploy smoke test, limited to tests that need no database seeding.
- **Feature traceability:** tag each test with the IDs in `docs/features.md`, for example `[Trait("Feature", "F-020")]`, so audits can map tests to features.
- **Async assertions** (webhook delivery) use a poll-until-timeout helper, never fixed sleeps.

## Blockers to settle first
1. **Migrations are not applied at startup.** Nothing in `Presentation` calls `Migrate()`; operators apply them separately (`docs/operations.md`, "Database migrations"). After PostgreSQL starts, the fixture must apply migrations before it starts the API container, either through `ApplicationDbContext.Database.MigrateAsync()` against the container's connection string or with an idempotent script. The E2E suite becomes the first automated run of the real migrations.
2. **Identity tokens are only logged.** `Application/Services/IdentityNotificationService.cs` logs that a verification, invite or reset notification was queued, but never delivers the token. Registration returns `202` and the tenant stays `PendingVerification` until `/api/v1/auth/verification/complete` runs with that token. So **a purely black-box test can't get a usable tenant at all**. Options:
   - **Interim (recommended to start):** a grey-box arrange step. The fixture seeds a verified, `Active` tenant and admin user directly in PostgreSQL through `Infrastructure`, and every act and assert step then goes through HTTP. The registration journey itself (F-001, F-006) stays out of E2E until option 2 exists.
   - **Target:** a real SMTP `IIdentityNotificationService` plus a **Mailpit** container. Tests read the token from Mailpit's API. That is a product feature in its own right, with its own plan, configuration and docs.
   - **Rejected:** a test-only endpoint that returns tokens. It is an attack surface the feature auditor should flag.

## Journeys (mapped to `docs/features.md`)
1. **Session lifecycle:** F-002 login → F-009 tenant details → F-003 refresh → F-004 list and revoke sessions → the revoked session's refresh token is rejected.
2. **Isolation on real PostgreSQL (F-020):** two tenants. Tenant A's token with tenant B's `X-Tenant-ID` or `Host` is rejected, and none of B's resources are visible to A.
3. **Rate limiting with real Redis (F-021):** exceeding the plan limit returns `429`; after a plan upgrade (F-012) the higher limit applies.
4. **Billing ingestion (F-015 → F-013, F-014):** a signed `POST /api/internal/billing/subscription-events` (signing format in `docs/contracts.md`, headers `X-Billing-Timestamp` and `X-Billing-Signature`) changes billing status, and replaying the same event ID is idempotent.
5. **Operator suspension (F-011):** a signed suspend (`X-Operations-Timestamp`, `X-Operations-Signature`) blocks login and authenticated `/auth` routes; reactivate restores access.
6. **Outbound webhooks (F-016, F-017):** register an endpoint pointing at WireMock, trigger an event, and poll until a correctly signed delivery arrives.
7. **Audit and health (F-018, F-023):** after journey 1 the tenant audit log holds the expected entries, and `/health` reports healthy with PostgreSQL checked.
8. **Later, after Mailpit:** registration and verification (F-001, F-006), invitations (F-005), password reset (F-007), MFA enrollment (F-008).

## Steps
- [ ] **Project.** Create `Tests.E2E` (xUnit v3, same target framework as `Tests`) and add it to the solution with `Testcontainers.PostgreSql`, `Testcontainers.Redis`, `Testcontainers` and `FluentAssertions`.
- [ ] **Stack fixture.** Write `E2EStack : IAsyncLifetime`. It creates a network, starts PostgreSQL and Redis, applies migrations, builds and starts the API image, and waits for `/health` to return `200`. Pass configuration as env vars with in-network host names:
  - `ConnectionStrings__DefaultConnection` and `Redis__ConnectionString`
  - `Jwt__Secret`, `Jwt__Issuer` and `Jwt__Audience`
  - `BillingIntegration__SharedSecret` and `TenantOperations__SharedSecret`
  - `TenantResolution__BaseDomain`

  It exposes `BaseAddress`.
- [ ] **First test.** `Health_ReportsHealthy` passes.
- [ ] **Tenant seeding.** Add an arrange helper that seeds a verified, active tenant and admin user, then logs in over HTTP.
- [ ] **Journeys 1–7,** one at a time, each with `Feature` traits.
- [ ] **CI.** Add a GitHub Actions job that runs `dotnet test Tests.E2E` on an Ubuntu runner, which has Docker. Keep it separate from the main test job.
- [ ] **Docs.** On completion, update `README.md` (how to run), `docs/operations.md` (Docker requirement, `E2E_BASE_URL`), `CLAUDE.md` (commands) and `docs/CHANGELOG.md`.

## Validation
- `dotnet build` and `dotnet test Tests.E2E` with Docker running.
- `dotnet test Tests` still passes without Docker.
- `git diff --check`.
