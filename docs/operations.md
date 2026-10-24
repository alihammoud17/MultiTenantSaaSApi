# Operations and Local Development

## Prerequisites

- .NET 10 SDK
- Node.js 22 or later and npm
- PostgreSQL 16 or later
- Redis 7 or later
- EF Core CLI at `/tmp/dotnet-tools/dotnet-ef` for repository scripts, or a working `dotnet ef` command for manual operations

## Standard local workflow

From the repository root:

```bash
scripts/dev.sh bootstrap
scripts/dev.sh seed
scripts/dev.sh run
```

In a second shell:

```bash
scripts/dev.sh smoke
scripts/dev.sh test
```

Use `scripts/dev.sh reset` before `seed` for a clean database and BillingService workflow state. `scripts/dev.sh help` lists supported commands. The wrapper dispatches to transparent scripts under `scripts/local/`.

### Command behavior

- `bootstrap`: restores/builds .NET, applies migrations when the configured EF tool is available, and installs BillingService dependencies with `npm ci`.
- `reset`: removes local service logs and BillingService state, drops the local database, and reapplies migrations.
- `seed`: reapplies migrations and reports the current manual boundary for scenario/demo data.
- `run`: starts both services in the foreground and captures local logs.
- `smoke`: checks API and BillingService health/metrics and placeholder webhook acceptance.
- `test`: runs the required .NET and BillingService validation suites.

Smoke success does not prove provider signature verification, authenticated callback delivery, production telemetry, or external dependency readiness.

## Configuration reference

### ASP.NET Core API

Configuration can use user secrets for development or double-underscore environment variables:

| Configuration key | Environment variable | Purpose |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | PostgreSQL connection string; required. |
| `Redis:ConnectionString` | `Redis__ConnectionString` | Redis rate-limit connection; required at startup. |
| `Jwt:Secret` | `Jwt__Secret` | JWT signing secret; required and sensitive. |
| `Jwt:Issuer` | `Jwt__Issuer` | Valid token issuer; default documentation value is `MultiTenantSaasApi`. |
| `Jwt:Audience` | `Jwt__Audience` | Valid token audience; default documentation value is `MultiTenantSaasApi`. |
| `Jwt:ExpirationMinutes` | `Jwt__ExpirationMinutes` | Access-token lifetime; documented default is `60`. |
| `BillingIntegration:SharedSecret` | `BillingIntegration__SharedSecret` | Internal callback HMAC key; required and sensitive. |
| `BillingIntegration:AllowedClockSkewMinutes` | `BillingIntegration__AllowedClockSkewMinutes` | Callback timestamp tolerance; default is `5`. |
| `TenantOperations:SharedSecret` | `TenantOperations__SharedSecret` | HMAC key for the operator suspend/reactivate endpoints; sensitive. Unset disables them. Must differ from the billing secret. |
| `TenantOperations:AllowedClockSkewMinutes` | `TenantOperations__AllowedClockSkewMinutes` | Accepted timestamp skew for operator requests; default `5`. |
| `TenantResolution:BaseDomain` | `TenantResolution__BaseDomain` | Domain tenants are served under, e.g. `example.com`. Only `<subdomain>.<BaseDomain>` hosts resolve a tenant by subdomain; reserved labels (`api`, `www`, ...) never do. Unset disables host-based resolution. |
| `ForwardedHeaders:KnownNetworks` | `ForwardedHeaders__KnownNetworks__0` | CIDR list of reverse-proxy networks whose `X-Forwarded-For`/`X-Forwarded-Proto` headers are trusted. Unset means only loopback is trusted. |
| `ForwardedHeaders:KnownProxies` | `ForwardedHeaders__KnownProxies__0` | Individual reverse-proxy IP addresses to trust, in addition to `KnownNetworks`. |
| `Cors:AllowedOrigins` | `Cors__AllowedOrigins` or `Cors__AllowedOrigins__0` | Browser origins allowed to call the API outside `Development`, e.g. `https://app.example.com`. One comma-separated value or an indexed list. Listed origins get credentials. Unset means only tenant-registered origins get CORS headers. Ignored in `Development`. See [CORS](#cors). |

Example development setup from `Presentation/`:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=saasapi;Username=postgres;Password=YOUR_PASSWORD"
dotnet user-secrets set "Redis:ConnectionString" "localhost:6379"
dotnet user-secrets set "Jwt:Secret" "YOUR_STRONG_SECRET"
dotnet user-secrets set "Jwt:Issuer" "MultiTenantSaasApi"
dotnet user-secrets set "Jwt:Audience" "MultiTenantSaasApi"
dotnet user-secrets set "Jwt:ExpirationMinutes" "60"
dotnet user-secrets set "BillingIntegration:SharedSecret" "YOUR_INTERNAL_SHARED_SECRET"
dotnet user-secrets set "BillingIntegration:AllowedClockSkewMinutes" "5"
```

### BillingService

| Variable | Default / status | Purpose |
| --- | --- | --- |
| `PORT` | `3001` | HTTP listen port. |
| `NODE_ENV` | `development` | Runtime environment label. |
| `SERVICE_NAME` | `billing-service` | Health, metrics, and log service name. |
| `BILLING_PROVIDER` | `placeholder` | Accepts `placeholder`, `stripe`, or `paddle`; all currently select the placeholder webhook adapter. |
| `WEBHOOK_SIGNING_SECRET` | unset | Intended provider webhook signature secret; live verification is not wired. |
| `DOTNET_CALLBACK_BASE_URL` | unset | Intended API callback base URL; default callback delivery is not wired. |
| `STRIPE_API_KEY` | unset | Required by the Stripe gateway when explicitly constructed; sensitive. |
| `STRIPE_API_BASE_URL` | `https://api.stripe.com` | Stripe API override for the gateway slice. |
| `WORKFLOW_STATE_PATH` | `.billing-workflow-state.json` in the working directory | Queue, retry, dedupe, and dead-letter JSON state. |
| `WORKFLOW_MAX_ATTEMPTS` | `3` | Attempts before dead-letter. |
| `WORKFLOW_INITIAL_BACKOFF_MS` | `1000` | Initial retry delay. |
| `WORKFLOW_MAX_BACKOFF_MS` | `30000` | Retry-delay cap. |
| `WORKFLOW_POLL_INTERVAL_MS` | `2000` | Worker polling interval. |
| `RECONCILIATION_INTERVAL_MS` | `300000` | Scheduled reconciliation interval. |

Do not infer runtime capability from a parsed variable: provider selection, callback delivery, and reconciliation readers remain scaffolded.

### Tenant subdomain data check

Registration rejects reserved and malformed subdomains, and `TenantMiddleware` never resolves reserved labels from the host. Tenants created before those rules may still hold such names. List them with this read-only PostgreSQL query, then decide case by case how to rename them; there is no automated migration. The reserved list mirrors `ReservedSubdomains` in `Application/Services/TenantRegistrationValidator.cs`.

```sql
SELECT "Id", "Name", "Subdomain", "Status", "CreatedAt"
FROM "Tenants"
WHERE "Subdomain" IN ('admin', 'api', 'app', 'assets', 'auth', 'billing', 'cdn', 'dashboard', 'docs', 'ftp',
                      'internal', 'localhost', 'mail', 'smtp', 'static', 'status', 'support', 'www')
   OR "Subdomain" !~ '^[a-z0-9]([a-z0-9-]*[a-z0-9])?$'
   OR "Subdomain" ~ '^[0-9]+$'
   OR length("Subdomain") NOT BETWEEN 3 AND 50
ORDER BY "CreatedAt";
```

### Email normalization check

Migration `NormalizeUserEmails` lowercases every stored `Users` and `UserInvites` email, because emails are now stored in canonical lowercase form. If two users have emails that differ only in case, the update violates a unique email index and the whole migration rolls back. Its `Down` is a no-op, because the original casing is not kept. Before applying it, run this read-only check:

```sql
SELECT lower("Email") AS "Email", count(*) AS "Users"
FROM "Users"
GROUP BY lower("Email")
HAVING count(*) > 1;
```

An empty result means the migration will apply. Otherwise, decide by hand which account keeps the address (rename or remove the others), then run the migration. Lowercasing uses PostgreSQL `lower()` and the app uses `ToLowerInvariant()`. They agree for ASCII addresses; a non-ASCII address could in rare cases be normalized differently.

### Pending tenant registrations

Self-registered tenants stay `PendingVerification` (`Status = 3`) until the admin completes email verification. A registration for an email that already has an account creates nothing and only logs that an existing-account notice was queued. `IdentityNotificationService` only logs that a verification was queued; it does not send email and deliberately does not log the token, so there is no live way to deliver it yet. Unverified registrations older than 48 hours are reclaimed only when a new registration requests the same subdomain or email; there is no background cleanup.

On a **local development database only**, activate a tenant you registered yourself by hand:

```sql
UPDATE "Users" SET "EmailVerifiedAt" = now() WHERE "TenantId" = '<tenant-id>';
UPDATE "Tenants" SET "Status" = 0, "UpdatedAt" = now() WHERE "Id" = '<tenant-id>' AND "Status" = 3;
```

To review pending registrations (read-only):

```sql
SELECT "Id", "Subdomain", "CreatedAt", "CreatedAt" < now() - interval '48 hours' AS "Reclaimable"
FROM "Tenants"
WHERE "Status" = 3
ORDER BY "CreatedAt";
```

### Suspending and reactivating a tenant

Suspension blocks the tenant's login, refresh, and every tenant-scoped request (including authenticated `/auth/*` routes), and revokes its refresh tokens. Data is kept. Requests are signed with `TenantOperations:SharedSecret`; see `docs/contracts.md` for the contract.

NGINX rejects `/api/internal/*` with `403`, so call the API on the Compose network. For example, from the VM:

```bash
TENANT_ID=<tenant-id>
SECRET=<TenantOperations secret>
BODY='{"reason":"Chargeback investigation #1234","actor":"ops:alice"}'
TS=$(date -u +%Y-%m-%dT%H:%M:%SZ)
SIG="sha256=$(printf '%s\n%s' "$TS" "$BODY" | openssl dgst -sha256 -hmac "$SECRET" | sed 's/^.* //')"

docker compose exec -T nginx wget -qO- --header "Content-Type: application/json" \
  --header "X-Operations-Timestamp: $TS" --header "X-Operations-Signature: $SIG" \
  --post-data "$BODY" "http://api:8080/api/internal/tenants/$TENANT_ID/suspend"
```

Use `/reactivate` to lift a suspension (the reason is optional there). Each request must be signed with a fresh timestamp: a timestamp not later than the tenant's last status change is rejected with `409`. Check the result in the tenant's audit log (`TENANT_SUSPENDED` / `TENANT_REACTIVATED`).

## Database migrations

From the repository root:

```bash
/tmp/dotnet-tools/dotnet-ef database update --project Infrastructure --startup-project Presentation
```

If the tool is installed on `PATH`, the equivalent is:

```bash
dotnet ef database update --project Infrastructure --startup-project Presentation
```

Run migrations before local startup after pulling schema changes. Do not edit generated migrations to solve documentation or startup problems.

## Direct service startup

API:

```bash
dotnet run --project Presentation
```

BillingService:

```bash
cd BillingService
npm ci
npm run dev
```

Swagger UI is available only in Development and Testing.

## Validation and tests

Required .NET checks:

```bash
dotnet --info
/tmp/dotnet-tools/dotnet-ef --version
dotnet restore
dotnet build --no-restore
dotnet test --no-build --verbosity normal
```

Most of `dotnet test` needs no PostgreSQL or Redis: the API integration suites use SQLite in memory. Tests that need the real services use Testcontainers (`Tests/Containers/`) and need a running Docker daemon; without one they are skipped, not failed:

- PostgreSQL (`postgres:16-alpine`): the fixture applies the real EF Core migrations to a template database once, and each test gets its own copy. Used by the outbound-webhook dispatcher tests and the PostgreSQL run of the entitlement resolution tests, which catch provider behaviour SQLite does not have (column lengths, query translation).
- Redis (`redis:7-alpine`): used by `RateLimitServiceRedisTests` and `RedisCorsOriginCacheTests`. Set `REDIS_TEST_CONNECTION_STRING` (e.g. `localhost:6379`) to use an existing Redis instead of a container.

Set `TESTCONTAINERS_POSTGRES_IMAGE` or `TESTCONTAINERS_REDIS_IMAGE` to pull the images from a registry mirror when Docker Hub is rate-limited. Collect coverage with `dotnet test --no-build --collect:"XPlat Code Coverage"` (coverlet, Cobertura output; add `--results-directory <dir>` to keep it outside the repository).

BillingService checks:

```bash
cd BillingService
npm ci
npm run build
npm run typecheck
npm test
```

The repository wrapper `scripts/dev.sh test` performs the standardized cross-service sequence.

## Health, metrics, and logs

### API

- `GET /health` returns JSON with correlation ID, duration, self status, and PostgreSQL status.
- Redis is not included in the health report.
- `GET /metrics` returns an in-memory JSON snapshot.

### BillingService

- `GET /health` returns service/configuration-safe status and request context.
- `GET /metrics` returns in-memory route/status counters.
- Health does not verify live provider access, API callback delivery, reconciliation readers, or state-file integrity under concurrent instances.

Both services use structured correlation-aware logging. Local run scripts write `.local-api.log` and `.local-billing.log`. Do not log JWTs, passwords, provider/API keys, HMAC secrets, signing secrets, or full sensitive payloads.

The JSON metrics endpoints are local diagnostics, not OpenTelemetry or Prometheus exporters. Production sinks, dashboards, alerts, and SLOs remain open decisions.

## Billing workflow recovery

The BillingService state file contains queue, retry, deduplication, and dead-letter records. Keep it on a writable persistent path and do not allow multiple uncontrolled writers.

For dead-letter triage:

1. Locate the event and correlation IDs in structured logs and workflow state.
2. Classify the failure as transient transport, permanent validation/mapping, configuration, or code failure.
3. Correct the cause before replay.
4. Confirm whether the event is already processed or dead-lettered.
5. Replay only the affected event in a controlled environment.
6. Verify event-id deduplication prevents duplicate side effects.

The default publisher only logs callback payloads, so successful local queue processing does not prove API delivery. The reconciliation algorithm is tested, but scheduled runtime sources currently return no records.

## Outbound-webhook operations

- Create/update endpoints only with tenant-authorized API actions.
- Disable an endpoint to contain an incident without deleting its history.
- Treat newly issued and pending signing secrets as sensitive; never put them in logs or tickets.
- Rotation initiation does not by itself complete active-secret cutover.
- Investigate retries using delivery ID, event ID, tenant ID, correlation ID, attempt count, HTTP status, and error category rather than payload secrets.

## Docker Compose local VM profile

`compose.yml` starts PostgreSQL 16, Redis 7, the API, BillingService, and NGINX. It persists PostgreSQL data and BillingService workflow state. NGINX publishes port 80:

- `http://localhost/health` routes to the API.
- `http://localhost/billing/health` and `/billing/metrics` route to BillingService with the `/billing/` prefix removed.
- `/api/internal/*` (any case) returns `403`. The internal billing callback and the operator tenant endpoints are reached only on the Compose network at `http://api:8080`.

Compose expects these runtime files outside Git:

```text
/etc/multitenant-saas-api/db.env
/etc/multitenant-saas-api/redis.env
/etc/multitenant-saas-api/api.env
/etc/multitenant-saas-api/billing.env
```

Copy and edit the placeholder templates under `deploy/env-examples/`. Never commit populated copies.

The Compose default network uses the fixed subnet `172.28.0.0/24`, and `api.env` must set `ForwardedHeaders__KnownNetworks__0=172.28.0.0/24`. Without it, the API sees every request as coming from the NGINX container, so all clients share one auth rate-limit bucket and audit/refresh-token IP fields record the proxy address. Only the last `X-Forwarded-For` entry (the one NGINX appends) is used, so client-supplied values cannot spoof the address. If you change the subnet, change both places.

```bash
docker compose config
docker compose up --build
```

This profile uses HTTP ingress and is intended for a local Ubuntu VM. TLS, public DNS, a non-`Development` CORS allowlist, external secret storage, backup/restore, production telemetry, and rollback rehearsals are not completed by running Compose.

## v1 deployment: Terraform and Ansible

The first deployment is one EC2 host, v1 of [the deployment roadmap](deployment-roadmap.md).

- `deploy/terraform/` provisions a VPC with one public subnet, an internet gateway, a security group and a `t3.small` Debian 12 instance. It targets the local Floci emulator by default. Steps, defaults and limitations are in [deploy/terraform/README.md](../deploy/terraform/README.md).
- `deploy/ansible/` configures that host: PostgreSQL 16, Redis 7, the ASP.NET Core 10 runtime, Node 22 and NGINX run natively, and the API and BillingService run as the `mtsaas-api` and `mtsaas-billing` systemd services. See [deploy/ansible/README.md](../deploy/ansible/README.md).

```bash
cd deploy/terraform && tofu init && tofu apply -var "ssh_public_key=$(cat ~/.ssh/mtsaas-ansible.pub)"
cd ../ansible && scripts/inventory.sh aws ~/.ssh/mtsaas-ansible
ansible-playbook -i inventories/aws playbooks/site.yml --ask-vault-pass     # full provision
ansible-playbook -i inventories/aws playbooks/deploy.yml --ask-vault-pass   # later releases
```

On the host:

| Item | Location |
| --- | --- |
| Runtime env files (`0600`, root) | `/etc/multitenant-saas-api/api.env`, `billing.env` |
| Releases and the active one | `/opt/multitenant-saas-api/{api,billing}/releases/<commit>/`, `.../current` |
| BillingService workflow state | `/var/lib/mtsaas-billing/billing-workflow-state.json` |
| Logs | `journalctl -u mtsaas-api`, `journalctl -u mtsaas-billing` |

Migrations run from the release's `efbundle` before `current` switches. To roll back the code, point `current` at the previous release and `systemctl restart mtsaas-api` (or `mtsaas-billing`); migrations aren't rolled back. Backups aren't automated yet (roadmap v1.1).

The Docker Compose profile above remains the local VM profile.

## CORS

The API applies one named policy, `ApiCorsPolicy` (`Presentation/Cors/ApiCorsPolicy.cs`), evaluated per request by `TenantAwareCorsPolicyProvider` (`Presentation/Cors/TenantAwareCorsPolicyProvider.cs`):

- **`Development`:** any origin, header, and method, without credentials. `Cors:AllowedOrigins` and tenant origins are ignored.
- **Every other environment** (including `Production`, `Staging`, and the test host's `Testing`):
  1. An origin in `Cors:AllowedOrigins` is echoed in `Access-Control-Allow-Origin` with `Access-Control-Allow-Credentials: true`, and any header and method are allowed.
  2. Otherwise, an origin registered by an **active** tenant (see [Tenant-registered origins](#tenant-registered-origins)) is echoed **without** credentials, with any header and method, and with `Vary: Origin`.
  3. Any other origin gets no CORS headers, so the browser blocks the response. Preflights still answer `204`. With nothing configured and no tenant origins, no origin is allowed.

Set origins with either form:

```bash
Cors__AllowedOrigins=https://app.example.com,https://admin.example.com
# or
Cors__AllowedOrigins__0=https://app.example.com
Cors__AllowedOrigins__1=https://admin.example.com
```

Each entry must be a full `http`/`https` origin: scheme, host, and optional port, with no path, query, or wildcard. A trailing `/` is dropped. The API refuses to start if an entry is invalid. Matching is exact, so `http://` and `https://`, a different port, and a different subdomain are all different origins.

The Compose `api.env.example` sets `ASPNETCORE_ENVIRONMENT=Development`, which keeps the permissive policy. Set another environment and `Cors__AllowedOrigins` before exposing a browser client.

The v1 Ansible deployment runs the API as `Production` and writes `Cors__AllowedOrigins` from the `api_cors_allowed_origins` list in `deploy/ansible/inventories/<env>/group_vars/all/main.yml`. It is empty by default, so no browser origin is allowed until you set it and rerun `playbooks/deploy.yml`.

### Tenant-registered origins

Tenant administrators manage their own origins under `/api/v1/admin/tenant/cors-origins`:

| Method | Route | Permission | Result |
| --- | --- | --- | --- |
| `GET` | `/api/v1/admin/tenant/cors-origins` | `tenants.read` | `200` with `[{ id, origin, createdAt }]` for the caller's tenant |
| `POST` | `/api/v1/admin/tenant/cors-origins` with `{ "origin": "https://app.example.com" }` | `tenants.manage` | `201` with the stored origin; `400` if invalid or the tenant already has 20; `409` if already registered |
| `DELETE` | `/api/v1/admin/tenant/cors-origins/{id}` | `tenants.manage` | `204`, or `404` if the id is not the caller's |

All three also require the `feature.security.cors_origins.manage` entitlement, which both seeded plans grant; a tenant override set to `false` turns it off. Adds and removals are audited as `CORS_ORIGIN_ADDED` and `CORS_ORIGIN_REMOVED`.

An origin must be exactly `https://host[:port]` as a browser sends it: no path (not even a trailing `/`), query, fragment, credentials, wildcard, explicit `:443`, or non-ASCII host (use punycode). `null` is rejected. Case is ignored and the origin is stored lowercase. `http://localhost[:port]` is accepted only when the API runs as `Development`; outside it, `localhost` is rejected under any scheme.

Which tenants' origins apply to a request:

- **Host is a tenant subdomain** (`<subdomain>.<TenantResolution:BaseDomain>`): only that tenant's origins, and only while the tenant is active. An unknown subdomain gets no tenant origins.
- **Any other host** (for example a shared `api.` host): origins registered by any active tenant. A preflight carries no credentials or tenant header, so the tenant cannot be identified there. No public route carries the tenant in its path, so the path is not used.

Because an origin one tenant registers is then accepted on shared hosts for every tenant's routes, tenant origins never get `Access-Control-Allow-Credentials`. The API authenticates with bearer tokens, not cookies, so this does not limit normal clients; a browser request sent with `credentials: "include"` from a tenant origin is blocked.

#### Origin cache

The lists are cached in Redis (`Application/Services/RedisCorsOriginCache.cs`) for 5 minutes, one list for all active tenants and one per tenant subdomain, under `cors:tenant-origins:<generation>:<scope>`. Adding or removing an origin, and suspending or reactivating a tenant, increments `cors:tenant-origins:generation`, so every list reloads from PostgreSQL on its next use. A change made directly in the database is picked up within 5 minutes, or at once with `redis-cli INCR cors:tenant-origins:generation`.

If Redis is unreachable, every cross-origin request with an unlisted origin reads PostgreSQL instead and a warning is logged. If Redis fails during an invalidation, a removed origin can stay allowed until its cached list expires (at most 5 minutes); the warning `CORS origin cache invalidation failed` marks this.

## Troubleshooting order

1. **Restore/install:** rerun the failing `dotnet restore` or `npm ci` directly.
2. **EF tooling/migration:** verify the EF tool path and PostgreSQL connection, then rerun database update.
3. **Startup:** inspect `.local-api.log` and `.local-billing.log`; fix the first exception.
4. **Port mismatch:** keep `API_URL` and `BILLING_URL` overrides consistent between run and smoke shells.
5. **Health/smoke:** verify both processes remain running and query their health endpoints directly.
6. **Tests:** rerun the exact failing service command before rerunning the full wrapper.
