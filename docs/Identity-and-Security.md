# Identity and Security (V3 hardening iteration)

This document captures the current identity-hardening baseline in the .NET API as of **April 10, 2026**, plus remaining follow-up items for later V3 slices.

## Scope and ownership

- The .NET API remains the system of record for tenant/user identity state, session state, RBAC authorization, and tenant isolation enforcement.
- Identity lifecycle and MFA step-up behavior are implemented in tenant-scoped API workflows and persisted in .NET-owned storage.
- `BillingService` does not own identity lifecycle, authentication, or tenant authorization responsibilities.

## Implemented identity-hardening baseline

### Identity lifecycle foundation

Implemented tenant-scoped flows:

- invite issuance and invite acceptance
- verification request and verification completion
- password-reset request and password-reset completion
- active session inventory and revoke-all refresh-token sessions

Security characteristics in this baseline:

- tokenized invite/verification/reset records are persisted using token hashes (not raw tokens)
- verification/reset tokens are single-use (`UsedAt` semantics) with explicit expiry windows
- lifecycle actions are tenant scoped and reject cross-tenant misuse
- notification delivery is abstracted behind `IIdentityNotificationService`

### MFA enrollment + step-up foundation

Implemented MFA and sensitive-action hardening:

- authenticated users can initiate and complete TOTP enrollment
- enrolled users can obtain short-lived step-up sessions for a specific purpose
- admin-sensitive endpoints require step-up when the acting user has MFA enabled
- step-up purpose binding is enforced to prevent token reuse across unrelated sensitive actions

### Tests and coverage focus

Current tests cover hardening behavior for:

- verification/password-reset replay resistance
- MFA step-up enforcement and purpose binding
- tenant isolation and authorization-denial paths on sensitive actions

## Secrets and environment variables

### Current required configuration (existing)

No new mandatory secrets or environment variables were introduced by this identity-hardening slice.

The API continues to require the existing JWT settings:

- `Jwt:Secret`
- `Jwt:Issuer`
- `Jwt:Audience`
- `Jwt:ExpirationMinutes`

Existing billing callback settings remain required for internal billing callbacks (unchanged by identity hardening):

- `BillingIntegration:SharedSecret`
- `BillingIntegration:AllowedClockSkewMinutes`

### Current placeholder behavior

- `IIdentityNotificationService` is currently a logging placeholder implementation.
- Because of that, no provider API key/secret is required yet for invite/verification/password-reset delivery.

### Future secret/env additions (when implemented)

When a live notification provider is wired in a later iteration, add provider-specific configuration such as:

- provider API key/secret
- sender identity/from-address
- optional callback signing secret (if provider webhooks are used)

Do not hardcode these values; use environment variables or user-secrets configuration.

## Remaining hardening backlog

- encrypt MFA secrets at rest
- add recovery-code issuance/rotation and revocation paths
- add anti-automation controls around public verification/reset endpoints
- add scheduled retention/cleanup for invite, verification, reset, enrollment, and step-up records
- connect identity notifications to a live provider with secure secret management

## Related docs

- `README.md`
- `docs/V3-Implementation-Backlog.md`
- `docs/Internal-Billing-Contract.md`
