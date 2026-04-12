# Versioning Strategy

## Purpose

This document defines how versioning works for this application across:

- public API contracts
- application releases
- Git workflow
- CI/CD deployments

The goal is to keep versioning simple, predictable, and maintainable throughout the application lifecycle.

---

## Core Principles

This project uses **three separate version concepts**, each with a different responsibility:

1. **API version**
   - Used by API consumers
   - Defines the public contract exposed by the application
   - Appears in the route

2. **Application release version**
   - Used to track the lifecycle of the application itself
   - Used for releases, tags, artifacts, and deployments
   - Follows Semantic Versioning (`MAJOR.MINOR.PATCH`)

3. **Git branches**
   - Used for source control workflow
   - Used to isolate feature work, release preparation, and hotfixes
   - Do **not** determine runtime API behavior

These concepts must remain separate.

---

## Public API Versioning

### Strategy

The API uses **URL-segment versioning**.

Example:

- `/api/v1/auth/login`
- `/api/v1/auth/refresh`
- `/api/v1/plans`
- `/api/v1/admin/tenant/users`

Future breaking versions will follow the same pattern:

- `/api/v2/...`
- `/api/v3/...`

### First Production Version

Although the repository historically contains branches such as `v1`, `v2`, `v3.0`, `v3.1`, and `master`, the application is not yet in production.

Therefore, the **first production public API version will be `v1`**.

Internal branch history does not define the external API version seen by clients.

### When to Create a New API Version

Create a new API major version only when there is a **breaking API contract change**, such as:

- request DTO changes that break existing clients
- response DTO changes that break existing clients
- route structure changes
- authentication/authorization behavior changes that break existing integrations
- semantic behavior changes that make existing clients incompatible

### When Not to Create a New API Version

Do **not** create a new API version for:

- bug fixes
- internal refactoring
- security improvements
- performance improvements
- backward-compatible new fields
- backward-compatible new endpoints

These changes should stay within the current API version.

### API Version Policy

- Public API versions are **major-only**
- Minor and patch changes belong to the **application release version**, not the route
- API routes should remain stable for clients within the same API major version

Example:

- App release `1.0.0` may expose `/api/v1/...`
- App release `1.1.0` may still expose `/api/v1/...`
- App release `1.2.3` may still expose `/api/v1/...`
- App release `2.0.0` may introduce `/api/v2/...`

---

## Application Release Versioning

### Strategy

Application releases follow **Semantic Versioning**:

`MAJOR.MINOR.PATCH`

Examples:

- `1.0.0`
- `1.0.1`
- `1.1.0`
- `2.0.0`

### Meaning

- **MAJOR**
  - breaking release
  - usually aligned with a new API major version when the public contract breaks

- **MINOR**
  - backward-compatible feature release

- **PATCH**
  - backward-compatible bug fix, security fix, or small correction

### Examples

- `1.0.0`
  - first production release
  - public API is `/api/v1/...`

- `1.0.1`
  - patch release
  - public API remains `/api/v1/...`

- `1.1.0`
  - new backward-compatible feature
  - public API remains `/api/v1/...`

- `2.0.0`
  - breaking release
  - may introduce `/api/v2/...`

---

## Git Branch Model

### Main Branches

The repository uses a lightweight branching model:

- `master`
  - primary development and integration branch

- `feature/*`
  - short-lived branches for new work

- `release/*`
  - optional short-lived stabilization branches before production release

- `hotfix/*`
  - short-lived urgent fix branches for production issues

### Examples

- `feature/api-versioning`
- `feature/billing-hardening`
- `release/1.0.0`
- `hotfix/1.0.1`

### Historical Branches

Historical branches such as:

- `v1`
- `v2`
- `v3.0`
- `v3.1`

are treated as **historical references**, not as the long-term branch model for runtime versioning.

They may be used to:

- inspect old behavior
- compare historical contracts
- recover older implementation details

They should **not** be used as the runtime mechanism for public API version selection.

---

## Tag Model

### Release Tags

Every production release must be tagged using Semantic Versioning.

Examples:

- `1.0.0`
- `1.0.1`
- `1.1.0`
- `2.0.0`

### Rules

- Tags represent immutable released versions of the application
- Production deployments should come from release tags, not arbitrary branch states
- Tags are the source of truth for released application versions

---

## CI/CD Flow

## Pull Requests

For every pull request targeting `master`:

- restore dependencies
- build solution
- run unit tests
- run integration tests
- run security/code scanning checks
- block merge if checks fail

## Development Deployment

Optionally, every successful merge to `master` may deploy automatically to a development environment.

## Release Flow

When preparing a production release:

1. create `release/x.y.z` from `master` if a stabilization phase is needed
2. run full CI pipeline
3. deploy to staging
4. validate smoke tests / manual checks
5. approve production deployment
6. create release tag `x.y.z`
7. deploy production from the tag

## Hotfix Flow

For urgent production issues:

1. create `hotfix/x.y.z`
2. implement the fix
3. run CI
4. deploy
5. tag the release
6. merge the fix back into `master`

---

## Route Format

### Business API

Use:

- `/api/v1/...`
- `/api/v2/...`

Examples:

- `/api/v1/auth/login`
- `/api/v1/auth/refresh`
- `/api/v1/plans`
- `/api/v1/admin/tenant/users`
- `/api/v1/tenant/audit-logs`

### Non-Versioned Operational Endpoints

The following may remain unversioned unless there is a strong reason to version them:

- `/health`
- `/swagger`
- `/metrics` (if added in the future)

---

## Swagger / OpenAPI

Swagger should be generated per API version.

Examples:

- `v1`
- `v2`

The Swagger UI should allow selecting the API version being explored.

---

## Decision Matrix

### Add a bug fix
- New API version: **No**
- New app release: **Yes, PATCH**
- Example: `1.0.1`

### Add backward-compatible endpoint
- New API version: **No**
- New app release: **Yes, MINOR**
- Example: `1.1.0`

### Add backward-compatible response field
- New API version: **No**
- New app release: **Yes, MINOR**
- Example: `1.2.0`

### Change request/response contract in a breaking way
- New API version: **Yes**
- New app release: **Yes, MAJOR**
- Example: `2.0.0`

### Security fix without contract change
- New API version: **No**
- New app release: **Yes, PATCH**
- Example: `1.0.2`

### Internal refactor
- New API version: **No**
- New app release: maybe, depending on release scope

---

## Recommended Starting Point

For this project:

- first production app release: `1.0.0`
- first production public API version: `/api/v1/...`
- primary active branch: `master`
- first release branch if needed: `release/1.0.0`

This keeps the public contract clean and avoids exposing internal historical branch numbering to API consumers.

---

## Summary

This project follows these rules:

1. Public API versions use route segments: `/api/v1/...`
2. The first public API version is `v1`
3. Application releases use Semantic Versioning: `MAJOR.MINOR.PATCH`
4. Git branches are for workflow, not runtime API behavior
5. Backward-compatible changes do not create a new API version
6. Breaking API changes create a new API major version
7. Production deployments come from tagged releases
