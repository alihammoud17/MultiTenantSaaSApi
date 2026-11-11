# Versioning Strategy

## Purpose

This document defines how versioning works across:

- public API contracts
- application releases
- Git workflow
- CI/CD deployments

The goal is to keep versioning predictable and maintainable while preparing for the first public production release.

---

## Core principles

This repository intentionally separates three concepts:

1. **Public API version** (client-facing contract)
2. **Application release version** (SemVer lifecycle)
3. **Git branches** (workflow)

These concepts must remain independent. Branch names do not determine runtime API behavior.

---

## Public API versioning

### Route format

Public API routes use URL-segment versioning:

- `/api/v1/...`
- `/api/v2/...` (only when needed)

Current production foundation:

- first public API version is **v1**
- existing public endpoints are exposed under `/api/v1/...`

Examples:

- `/api/v1/auth/login`
- `/api/v1/auth/refresh`
- `/api/v1/plans`
- `/api/v1/admin/tenant/users`
- `/api/v1/tenant/audit-logs`

### When to create a new API major version

Create a new API major version only for **breaking contract changes**, such as:

- request shape changes that break existing clients
- response shape changes that break existing clients
- incompatible route changes
- behavior changes that make existing client integrations incompatible

### When not to create a new API major version

Do not create a new API version for:

- bug fixes
- internal refactoring
- performance improvements
- security hardening that keeps contract compatibility
- backward-compatible new fields/endpoints

### Non-versioned operational endpoints

Operational/system endpoints may remain unversioned unless explicitly required otherwise:

- `/health`
- `/metrics`
- `/swagger`

---

## Application release versioning

### SemVer policy

Application releases follow Semantic Versioning (`MAJOR.MINOR.PATCH`):

- `1.0.0`
- `1.0.1`
- `1.1.0`
- `2.0.0`

### SemVer meaning

- **MAJOR**: breaking change release
- **MINOR**: backward-compatible feature release
- **PATCH**: backward-compatible fixes/security fixes

### API and release relationship

- multiple app releases can continue serving the same API major route
- a new API major route is introduced when a real breaking contract change occurs

Examples:

- `1.0.0` -> `/api/v1/...`
- `1.1.0` -> still `/api/v1/...`
- `1.2.3` -> still `/api/v1/...`
- `2.0.0` -> may add `/api/v2/...`

---

## Git branch model (workflow only)

Active model:

- `master`
- `feature/*`
- `release/*`
- `hotfix/*`

Rules:

- branches are used for collaboration and release preparation
- branches are **not** runtime API version selectors
- historical branch names (for example `v1`, `v2`, `v3.x`) are reference artifacts, not production API version controls

---

## Tag and deployment model

### Release tags

Every production release should be tagged with SemVer:

- `1.0.0`
- `1.0.1`
- `1.1.0`
- `2.0.0`

### Deployment policy

- production deployments should come from immutable release tags
- avoid deploying to production from arbitrary branch heads

---

## Swagger / OpenAPI

Swagger/OpenAPI should publish versioned documents.

Current baseline:

- `v1` document is generated and available

Future:

- additional docs (`v2`, etc.) are added only when new API majors are introduced

---

## CI/CD flow guidance

For pull requests targeting `master`:

- restore dependencies
- build solution
- run tests
- run required security/scanning checks
- block merge when checks fail

Release flow:

1. create `release/x.y.z` from `master` if stabilization is needed
2. run full CI pipeline
3. deploy to staging and validate
4. create release tag `x.y.z`
5. deploy production from the tag

Hotfix flow:

1. create `hotfix/x.y.z`
2. implement urgent fix
3. run CI
4. tag release
5. deploy from tag and merge back to `master`

---

## Decision matrix

### Add bug fix
- API major bump: **No**
- App release: **PATCH**

### Add backward-compatible endpoint/field
- API major bump: **No**
- App release: **MINOR**

### Introduce breaking request/response change
- API major bump: **Yes**
- App release: **MAJOR**

### Internal refactor without contract change
- API major bump: **No**
- App release: depends on release scope

---

## Summary policy (authoritative)

- API routes use `/api/v1/...` format
- first public API version is **v1**
- API major versions change only for breaking contract changes
- app releases use SemVer
- branches are workflow-only, not runtime version selectors
- production deployments come from release tags
