# Versioning Policy

This repository uses three separate versioning concepts. They have different purposes and must stay independent.

## 1) Public API versioning (client contract)

- API routes use URL-segment versioning: `/api/v1/...`
- The first public production API version is **v1**
- New API major versions (for example `/api/v2/...`) are introduced **only** for breaking contract changes
- Backward-compatible changes stay in the current major API route

Examples:

- `/api/v1/auth/login`
- `/api/v1/plans`
- `/api/v1/admin/tenant/users`

## 2) Application release versioning (delivery lifecycle)

Application releases follow Semantic Versioning (`MAJOR.MINOR.PATCH`):

- `1.0.0`
- `1.0.1`
- `1.1.0`
- `2.0.0`

Guidance:

- **PATCH** for backward-compatible fixes
- **MINOR** for backward-compatible features
- **MAJOR** for breaking changes

## 3) Git branches (workflow only)

Branch model:

- `master`
- `feature/*`
- `release/*`
- `hotfix/*`

Branches are for collaboration workflow only. They are **not** runtime API version selectors.

## Swagger / OpenAPI

Swagger/OpenAPI is generated per API version group.

- `v1` is currently published
- additional documents (such as `v2`) are added when a new API major version is introduced

## Deployment policy

Production deployments should come from **SemVer release tags**, not floating branch heads.

Examples:

- tag `1.0.0` -> deploy release `1.0.0`
- tag `1.0.1` -> deploy hotfix release `1.0.1`
