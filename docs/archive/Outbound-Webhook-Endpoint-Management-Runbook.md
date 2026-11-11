# Outbound Webhook Endpoint Management Runbook (V4)

_Last updated: May 1, 2026_

## Purpose

Runbook for currently implemented tenant-scoped outbound webhook endpoint-management operations and signing-secret rotation initiation.

## Scope and guardrails

- Applies to .NET API outbound tenant webhooks only.
- Tenant isolation is mandatory: operators must only act within validated tenant context.
- Never log or share raw signing-secret values.
- Documented steps reflect only implemented behavior as of May 1, 2026.

## Implemented management operations

Tenant admins can:

1. Create endpoint
2. List endpoints
3. Update endpoint properties
4. Enable/disable endpoint
5. Delete endpoint
6. Initiate signing-secret rotation (`next` secret issuance)

## Signing-secret rotation initiation behavior (implemented)

When rotation is initiated:

- system generates and stores a new pending secret as `NextSigningSecret`
- issuance timestamp is stored in `NextSigningSecretIssuedAtUtc`
- active outbound signing remains on current `SigningSecret`
- no automatic cutover/finalization occurs in this phase

## Operational checklist

### A) Endpoint creation/update

- Confirm request is authenticated and tenant-admin authorized.
- Verify endpoint belongs to current tenant context.
- Validate endpoint URL/metadata according to API validation rules.
- Confirm endpoint appears in tenant-scoped listing.

### B) Disable endpoint (incident containment)

- Use explicit disable action on affected endpoint.
- Verify state changed for target endpoint within same tenant context.
- Confirm delivery attempts stop for disabled endpoint according to dispatcher behavior.
- Record incident/audit context without exposing secret material.

### C) Rotation initiation

- Confirm endpoint is in correct tenant scope.
- Trigger rotation-init action.
- Verify pending-secret issuance metadata is populated.
- Communicate that active signing secret has not yet changed in this phase.

## Safe logging guidance

Allowed:

- tenant identifier
- endpoint identifier
- correlation/request identifiers
- lifecycle status transitions (enabled/disabled, rotation initiated)

Forbidden:

- raw current signing secret
- raw pending signing secret
- HMAC values beyond existing transport headers/persisted diagnostics needed for troubleshooting

## Follow-up for future phases

- secret promotion/finalization endpoint + controlled cutover sequence
- rollback and dual-secret overlap policy (if required)
- automated rotation schedules and expiration/SLO monitoring
- enriched replay and endpoint-delivery troubleshooting workflows
