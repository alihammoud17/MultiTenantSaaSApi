# Outbound Webhooks (V3 Foundation + Endpoint Management Finalization)

_Last updated: May 1, 2026 (endpoint-management slice finalized)_

## Scope and status

This document tracks the currently implemented outbound tenant webhook capabilities in the .NET API.

Current status:

- outbound delivery foundation is implemented (versioned envelope, signed delivery, retries, durable status)
- tenant-scoped endpoint management API is implemented (`create/list/update/delete`, enable/disable)
- signing-secret rotation initiation is implemented via explicit `next` secret issuance
- active-delivery secret cutover/finalize workflow is intentionally deferred to a future phase

For payload/signature details, see `docs/Outbound-Webhook-Contract.md`.

## Ownership and architecture boundary

Outbound webhook publication and endpoint management in this iteration are owned by the .NET API because events are derived from internal tenant/subscription state.

`BillingService` remains the provider-facing boundary for inbound provider webhooks and provider-specific workflows.

## Implemented endpoint-management capabilities

Tenant-admin scoped management API (authenticated, tenant-scoped, RBAC enforced):

- create webhook endpoint
- list current tenant webhook endpoints
- update endpoint metadata/URL shape
- delete endpoint
- explicit enable/disable state management
- explicit signing-secret rotation initiation (issues `NextSigningSecret` + issuance timestamp)

Rotation behavior currently implemented:

- current active delivery signing material remains `SigningSecret`
- rotation-init action only pre-provisions `NextSigningSecret` and `NextSigningSecretIssuedAtUtc`
- current outbound signing semantics do not switch automatically in this phase
- raw secrets are never returned in listing/management responses after creation-time disclosure semantics

## Tenant-isolation and security guarantees (explicit)

Implemented tests and service guards enforce:

- tenant A cannot list or mutate tenant B endpoints through identifier tampering
- member-role users cannot execute tenant-admin-only management actions
- unauthenticated requests cannot access endpoint-management operations
- outbound signatures continue using endpoint-bound signing material with stable delivery idempotency semantics

## Migration summary (implemented)

Schema support for endpoint management/rotation was added in:

- `Infrastructure/Migrations/20260501090000_AddWebhookEndpointManagementFoundation.cs`

This migration adds additive fields needed for safe endpoint lifecycle + pending secret issuance tracking and preserves existing outbound delivery compatibility.

## Operational runbook pointers

For operational endpoint lifecycle + signing-secret rotation initiation procedures, see:

- `docs/Outbound-Webhook-Endpoint-Management-Runbook.md`

## Follow-up work (future phase, intentionally not implemented)

1. activate/cutover flow to promote `NextSigningSecret` to current active signing material
2. rollback window semantics and explicit dual-secret overlap policy (if adopted)
3. operator automation for periodic secret hygiene and incident-driven forced rotation
4. tenant self-service delivery replay tooling and richer endpoint diagnostics
