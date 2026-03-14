# AGENTS.md

## Project overview
This repository contains a multi-tenant SaaS API.

### Current V1 scope
- Tenant registration/authentication
- Plan model and upgrade flow
- Tenant context enforcement middleware
- Plan-based API usage limits
- Tenant-scoped audit logs
- Health checks
- Automated tests

### V2 target areas
- Refresh tokens and revocation
- Role/permission model (RBAC)
- Admin endpoints for tenant/user management
- Billing integration
- Background processing for subscription lifecycle
- Optional Node.js companion service for billing/webhooks/jobs
- Observability improvements

## Stack
- ASP.NET Core Web API
- EF Core
- SQL database
- Automated tests in the existing test project(s)

## Architecture rules
- Respect the current project structure and naming conventions.
- Keep changes incremental and reviewable.
- Do not rewrite V1 working code unless required for V2.
- Preserve backward compatibility where reasonable.
- Keep core tenant enforcement in .NET.
- Keep tenant isolation explicit in queries, services, and endpoints.
- Prefer clean code and small diffs over broad refactors.

## Multi-tenant safety rules
- Never return cross-tenant data.
- Every new endpoint must enforce tenant scoping.
- Every new admin capability must require authorization.
- Auth/session changes must preserve tenant context.
- Tests must cover tenant isolation for new behavior.

## Implementation expectations
For any non-trivial task:
1. Analyze current implementation first.
2. Propose touched files/classes before large edits.
3. Implement in the smallest safe slice.
4. Add or update tests.
5. Run validation commands.
6. Summarize changed files, risks, and follow-up work.

## Validation
Run the relevant commands after changes:
- dotnet build
- dotnet test

If schema changes are introduced:
- add EF Core migration
- note any migration naming decisions

## Coding guidelines
- Prefer extension over replacement.
- Avoid unrelated refactors.
- Keep DTOs, services, controllers, and persistence concerns separated.
- Keep error responses consistent with existing API behavior.
- Use existing patterns for middleware, dependency injection, and tests.

## Done when
A task is complete only when:
- the requested behavior is implemented
- build passes
- tests pass or any failing tests are clearly explained
- changed files are summarized
- any remaining follow-up work is listed explicitly