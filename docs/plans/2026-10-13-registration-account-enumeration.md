# Plan: close account enumeration on registration (audit finding 5)

- **Date:** 2026-10-13
- **Branch:** `fix/registration-account-enumeration` (off up-to-date `master`)
- **Goal:** Stop anonymous callers from learning whether an email has an account, through the register response or login timing. Document the remaining signal on authenticated admin paths.
- **Status:** done
- **Approval:** approved in plan mode

## Context
Audit finding 5 (Medium) from the tenant provisioning review: anyone can learn whether an email has an account.
- The anonymous `POST /api/v1/auth/register` answers `400 "Email already registered"` (`Presentation/Controllers/AuthController.cs:51`). Emails are globally unique, so this tells any internet caller whether the address exists in *any* tenant.
- `LoginAsync` (`Application/Services/AuthOrchestrationService.cs`) skips `BCrypt.Verify` when the email is unknown. An unknown email therefore answers measurably faster than a wrong password, which is a timing oracle on the same question.
- Tenant admins get the same signal from `POST /auth/invites` (`"User already exists for tenant"`, `AuthController.cs:218`) and from add-user (`"User already exists"`, `AdminTenantManagementController.cs:132`).

PR #141 (merged) made registration return `202` with no tokens. That makes an indistinguishable response possible: a duplicate email can get the same `202` as a real registration.

## Steps
- [x] **Register: answer a known email exactly like a new registration.** In `AuthOrchestrationService.RegisterAsync`, keep the order: validate → reclaim → subdomain check → email check. The subdomain check must stay first so its answer never depends on the email. When the email already exists:
  - still compute `BCrypt.HashPassword(request.AdminPassword)` and discard it, so the response time matches the real path, which BCrypt dominates;
  - send the owner an "account already exists" notice (next step);
  - write nothing to the database;
  - return a success result built with the same `RegistrationPendingResponse(Guid.NewGuid(), "pending_verification", now + PendingRegistrationLifetime)` shape.

  Remove `AuthFlowError.EmailAlreadyRegistered` and its controller mapping, because nothing produces it any more. A client that calls `/auth/verification/complete` with the random tenant id gets the existing generic `400 "Invalid or expired verification token"`.
- [x] **Notice to the real owner.** Add `SendExistingAccountRegistrationNoticeAsync(string email, CancellationToken)` to `Domain/Interfaces/IIdentityNotificationService.cs`.
  - `Application/Services/IdentityNotificationService.cs` logs that a notice was queued, without the email, matching its other methods.
  - Implement it in the test doubles `Tests/Integration/CapturingIdentityNotificationService.cs` (which records it per email) and `RecordingIdentityNotificationService` in `Tests/UnitTests/IdentityLifecycleServiceTests.cs`.
  - A send failure is logged and doesn't change the response, as for the verification send.
- [x] **Login: constant-time unknown-email path.** In `LoginAsync`, when no user matches, run `BCrypt.Verify` against a static dummy hash before returning `InvalidCredentials`. Generate the dummy hash once with the same work factor as real hashes. The response body already matches the wrong-password case.
- [x] **Admin paths: accurate wording, documented residual risk.** These callers are authenticated, RBAC-gated (`UsersManage`, step-up for invites), audited (`USER_INVITE_CREATED` records the email) and plan rate-limited.
  - Change the invite error from the inaccurate `"User already exists for tenant"` to `"Email is already in use"`.
  - Change the add-user error to the same text.
  - Don't redesign these flows: hiding the signal there would need breaking changes. See Out of scope.
- [x] **Docs.**
  - `docs/architecture.md` (registration paragraph): a duplicate email gets the same `202` plus an owner notice; login is constant-time; residual risks are the admin paths and the subdomain-retry signal (see Decisions).
  - `docs/operations.md` (pending registrations section): the existing-account notice is logged only, like the other notifications.
  - `docs/CHANGELOG.md` (Unreleased): register no longer returns `400 "Email already registered"`.

## Tests
- **Integration** (`Tests/Integration/ApiEndpointsTests.cs`):
  - Rewrite `Register_ShouldReturnBadRequest_WhenEmailAlreadyRegisteredWithDifferentCase`. A second registration with the same email in a different case gets `202`, with the same JSON property names as the first response. No tenant or user is created for it, the returned tenant id doesn't exist, and the owner notice was captured for that email.
  - Using the returned tenant id with `/auth/verification/complete` returns the generic `400`.
  - In `Register_ShouldStillBlockEmailAndSubdomain_WhilePendingRegistrationIsWithinItsWindow`, the same-email request now gets `202`, and the original pending tenant is untouched and still the only one holding the email. The subdomain branch still returns `400 "Subdomain already taken"`.
  - A taken subdomain combined with an existing email still returns `400 "Subdomain already taken"`, so the answer doesn't depend on the email.
  - Login with an unknown email and login with a wrong password return identical status and body.
- **Integration**: update the invite and add-user duplicate assertions to `"Email is already in use"`, wherever existing tests assert the old text.
- **Unit** (`Tests/UnitTests/AuthControllerBoundaryTests.cs`): remove or adjust any mapping test for `EmailAlreadyRegistered`.
- **Run:** `dotnet test` (full suite; the notification interface changes for every test double).

### Verification
- `dotnet restore && dotnet build --no-restore`: 0 errors.
- `dotnet test --no-build`: all pass; report the counts.
- `git diff --check`.
- Mutation check: restore the old early `EmailAlreadyRegistered` return and confirm the new register tests fail.

## Decisions & trade-offs
- **Fake `202` with a random tenant id** rather than a different status. It is the only way the anonymous response can be identical. The cost is that a real owner who forgot they registered gets a `202` that never verifies. The notice email covers that once delivery is live.
- **Timing parity through a discarded BCrypt hash.** BCrypt dominates the real path, so the remaining difference (a few inserts) is small next to network jitter. A full constant-time guarantee is not claimed.
- **Residual signal: subdomain retry.** A prober can register `x@victim` with a fresh subdomain S, then try S again with another email. A real registration reserved S (`400`); a fake one didn't (`202`). This costs two requests per probe from the shared 10/min register-and-login budget, and creates a pending tenant only when the email is unused. Closing it would mean reserving subdomains for fake registrations, which lets anyone hold names without an inbox. Documented, not fixed.

## Alternatives considered
- **Hide the signal on admin paths too.** That means always creating the invite, removing the invite token from the `POST /auth/invites` response (the admin could otherwise accept it themselves and see the failure), and making add-user fail generically. Rejected for this slice: it is breaking and only exposes the signal to authenticated, audited admins.
- **Generic `400` for both a taken subdomain and a taken email.** Rejected: subdomains are public in hostnames, and a vague error hurts legitimate sign-ups without hiding anything.

## Out of scope
- The admin-path redesign above.
- Real email delivery for `IdentityNotificationService`.
- The concurrent-registration unique-index race that returns `500` (audit Low).
- Audit finding 7 (suspended tenants on authenticated `/auth/*` routes).

## Changes
- **2026-10-13 — Implemented** on `fix/registration-account-enumeration`. No change to the approach. Details:
  - The known-email branch lives in `AuthOrchestrationService.AnswerExistingAccountRegistrationAsync`. The login dummy hash is a static `DummyPasswordHash`, created with `BCrypt.HashPassword` at its default work factor, the same as real hashes.
  - No existing test asserted the old invite or add-user error text, so those wording changes have no test updates. `IdentityLifecycleService` still throws `"User already exists."` internally; only the API text changed.
  - `AuthControllerBoundaryTests` had no `EmailAlreadyRegistered` mapping test, so nothing there needed changing.
  - Mutation check: putting back an early failure for a known email makes the two new or updated register tests fail.
  - Login timing is not asserted by tests, because wall-clock assertions are flaky. The test checks that the status and body are identical, and the dummy verify was checked by review.
