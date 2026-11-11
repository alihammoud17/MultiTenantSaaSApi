# Plan: fix the Low findings from the tenant provisioning audit

- **Date:** 2026-10-13
- **Branch:** two PRs off up-to-date `master`: `fix/auth-audit-actor-and-token-expiry` (PR A), then `fix/registration-race-and-email-normalization` (PR B, started after A merges).
- **Goal:** Make auth audit entries record the acting user and return the real access-token expiry (PR A). Make simultaneous registrations get the normal answers instead of a 500, with emails stored lowercase (PR B).
- **Status:** done
- **Approval:** approved in plan mode

## Context
The provisioning audit's two Low findings cover three issues, all still open on `master` (`cfa33a7`):
1. **Audit entries for anonymous auth flows have no actor.** `AuditService.LogAsync` takes the user id from `HttpContext.User`. Register, login, refresh and invite acceptance are anonymous requests, so `TENANT_REGISTERED`, `USER_LOGGED_IN`, `USER_TOKEN_REFRESHED` and `USER_INVITE_ACCEPTED` are saved with `UserId = Guid.Empty`. The real user id only appears inside the JSON.
2. **Wrong access-token expiry in responses.** `JwtService.GenerateToken` signs tokens with `Jwt:ExpirationMinutes`. Login (`AuthOrchestrationService.cs:262`) and refresh (`:305`) return a hard-coded `+60 min` as `expiresAt`.
3. **Simultaneous registrations return 500, and emails aren't case-normalized in storage.**
   - The subdomain and email checks run before the insert. Two requests at the same moment can both pass. The unique index then rejects the second, and the generic `catch` returns `500 "Registration failed"`. Since #142, that 500 also differs from the fake `202` a known email gets.
   - Emails are only trimmed, while `IX_Users_Email` is case-sensitive. A race between `Bob@x.com` and `bob@x.com` can create two users whose emails differ only in case. Login uses exact `==`, so the stored casing must be typed exactly.

None of these affects tenant isolation. Delivery is two thin PRs grouped by area: **A** auth responses (issues 1 and 2), then **B** registration race and email normalization (issue 3).

## Steps

### PR A: auth audit actor and real token expiry
- [x] **Explicit audit actor.** Add an optional `Guid? actorUserId = null` to `IAuditService.LogAsync` (`Domain/Interfaces/IAuditService.cs`). `Application/Services/AuditService.cs` uses it when given and otherwise falls back to the claims, so existing callers are unaffected. Pass the known user id from:
  - `RegisterAsync`: the new admin's `user.Id`;
  - `LoginAsync` and `RefreshAsync`: `user.Id`;
  - `AuthController.AcceptInvite`: `userId`.

  The operator `TENANT_SUSPENDED` and `TENANT_REACTIVATED` entries keep `Guid.Empty` by design, because no user exists for them.
- [x] **Real expiry from the JWT service.** Change `IJwtService.GenerateToken` to return a new record `AccessToken(string Token, DateTime ExpiresAt)` (in `Domain/Outputs`). `JwtService` uses one computed expiry for both the token's `exp` and the returned value. `LoginAsync` and `RefreshAsync` return `accessToken.ExpiresAt` in `AuthResponse` instead of `AddMinutes(60)`. Update the one test caller (`Tests/Integration/ApiEndpointsTests.cs`, pending-tenant JWT test) to use `.Token`.
- [x] **Docs.** `docs/architecture.md`: one sentence saying the auth audit entries record the acting user and the response expiry follows `Jwt:ExpirationMinutes`. `docs/CHANGELOG.md`: an Unreleased line.

### PR B: registration race and canonical lowercase emails
- [x] **One email normalizer.** Add `Domain/Identity/EmailAddress.cs` with `Normalize(string) => Trim().ToLowerInvariant()`. Use it at every write and lookup site, so stored emails are always lowercase:
  - `TenantRegistrationValidator` (normalized admin email);
  - `AuthOrchestrationService`: the login lookup, the registration duplicate check and the reclaim query;
  - `IdentityLifecycleService`: invite create and duplicate check, invite accept, verification request and password-reset request;
  - `AdminTenantManagementController`: the add-user check and insert.

  Once stored data is lowercase, replace the `u.Email.ToLower() == x` comparisons with plain `==`, so they can use the indexes.
- [x] **Data migration `NormalizeUserEmails`.** Raw SQL: `UPDATE "Users" SET "Email" = lower("Email") WHERE "Email" <> lower("Email")`, and the same for `"UserInvites"`. `Down` is a no-op, because the original casing can't be restored. If two existing users differ only in case, the update violates `IX_Users_Email` and the migration rolls back. `docs/operations.md` documents a read-only check to run first (`SELECT lower("Email"), count(*) FROM "Users" GROUP BY 1 HAVING count(*) > 1`) and says such duplicates are resolved by hand. With all emails lowercase, the existing case-sensitive unique index enforces uniqueness regardless of case, so no index change is needed.
- [x] **Map lost races to the normal answers.** In `RegisterAsync`, catch `DbUpdateException` from the transactional insert. Roll back, clear the change tracker, and run the checks again:
  - subdomain now taken → `400 "Subdomain already taken"`;
  - email now taken → the existing `AnswerExistingAccountRegistrationAsync` (the same `202` as #142, which closes the 500-versus-202 signal);
  - otherwise → `500 "Registration failed"`, as today.

  This works on any database provider; it doesn't parse constraint names.
- [x] **Docs.**
  - `docs/architecture.md`: emails are stored lowercase, and a lost registration race gets the normal answer.
  - `docs/operations.md`: the pre-migration duplicate check.
  - `docs/CHANGELOG.md`: an Unreleased line (stored emails are lowercased and login ignores case; migration `NormalizeUserEmails`).

## Tests

### PR A
- **Integration** (`ApiEndpointsTests` or `TenantLifecycleAndResolutionTests`): after register, verify and login, the `TENANT_REGISTERED`, `USER_LOGGED_IN` and `USER_TOKEN_REFRESHED` rows have `UserId` equal to the admin's id. `USER_INVITE_ACCEPTED` has the new user's id. Read the rows with `IgnoreQueryFilters()`.
- **Integration**: in a factory with `Jwt:ExpirationMinutes = 15` (via `WithWebHostBuilder`), login and refresh `expiresAt` equals the token's `exp` claim (read with `JwtSecurityTokenHandler.ReadJwtToken`) and is about 15 minutes from now, not 60.
- **Unit**: update the `IAuditService` stub in `Tests/UnitTests/AuthControllerBoundaryTests.cs` to the new signature.
- **Run:** `dotnet test`.

### PR B
- **Unit** (new `Tests/UnitTests/RegistrationRaceTests.cs`): `AuthOrchestrationService` on a shared-cache SQLite in-memory database. The EF in-memory provider used elsewhere doesn't enforce unique indexes or transactions, so it can't be used here. A test `ITransactionInterceptor` inserts a competing row through a second connection in `TransactionStartingAsync`, which runs after the checks and before `BEGIN`:
  - a competing tenant with the same subdomain → `SubdomainAlreadyTaken`, and nothing from the losing request persists;
  - a competing user with the same email → a success result with a tenant id that doesn't exist, the existing-account notice sent, and no second user;
  - a failure that isn't a conflict (e.g. the interceptor throws a non-unique `DbUpdateException`) → `RegistrationFailed`.
- **Integration** (`ApiEndpointsTests`):
  - registering with `Bob@Example.COM` stores `bob@example.com`;
  - login with `BOB@example.com` succeeds;
  - an invite to `Mixed@Case.com` is stored lowercase, and accepting it creates a lowercase user;
  - admin add-user with a different-case duplicate returns `"Email is already in use"`.
- **Migration check** (manual, in the PR): apply all migrations to a throwaway `postgres:16` Docker container with `dotnet ef database update`. Seed mixed-case users and invites, apply `NormalizeUserEmails`, and confirm they're lowercase. Then seed two users that differ only in case and confirm the migration fails and rolls back. If Docker isn't available, report it as not run.
- **Run:** `dotnet test`, and `dotnet ef migrations has-pending-model-changes` shows no drift.

### Verification (each PR)
- `dotnet restore && dotnet build --no-restore`: 0 errors.
- `dotnet test --no-build`: all pass; report the counts.
- Mutation check:
  - PR A: put back the hard-coded `AddMinutes(60)` and drop the actor argument; the new tests fail.
  - PR B: remove the `DbUpdateException` mapping; the race tests fail.
- `git diff --check`.

## Decisions & trade-offs
- **Optional `actorUserId` parameter.** It's explicit, only touches the four call sites that know the user, and leaves the other `LogAsync` callers unchanged.
- **Return the expiry from `JwtService`**, so the response and the token can never disagree.
- **Lowercase canonical storage.** It works the same on SQLite (so tests exercise it) and Postgres, and makes login ignore case. The trade-off is that the user's original casing isn't kept. The local part of an email is technically case-sensitive, but providers treat it as case-insensitive in practice. `ToLowerInvariant` and Postgres `lower()` agree for ASCII; a non-ASCII address could be normalized differently by the migration than by the app (edge case, noted in docs).
- **Re-run the checks after a failed insert.** It's simple and works on any provider, at the cost of two extra queries on a rare path.
- **Two PRs grouped by area** (auth responses, then registration race), so the migration ships in its own small change.

## Alternatives considered
- **Setting `HttpContext.User` for the audit actor** instead of an explicit parameter: rejected in favour of the explicit, narrower change.
- **Reading `Jwt:ExpirationMinutes` in the orchestrator:** rejected, because the response and the token could still disagree.
- **A Postgres `lower("Email")` functional unique index or a `citext` column** instead of lowercase storage: rejected. Neither is exercised by the SQLite tests, and login would still need case-insensitive lookups.
- **Parsing provider-specific constraint names** to classify the unique violation: rejected in favour of re-running the checks, which works on any provider.

## Out of scope
- A database `CHECK ("Email" = lower("Email"))` constraint.
- Auditing verification and password-reset completion, which write no audit entries today.
- Other audit observations not rated Low: roles not seeded at registration, free-text invite roles, the seat limit.

## Changes
- **2026-10-13 — PR A implemented** on `fix/auth-audit-actor-and-token-expiry`. No change to the approach. Details:
  - `JwtService` returns `token.ValidTo`, the `exp` claim truncated to whole seconds, so the returned `expiresAt` equals the token's `exp` exactly.
  - Tests added: `AnonymousAuthFlows_ShouldRecordTheActingUserInAuditLog` and `LoginAndRefresh_ShouldReturnTheTokensRealExpiry_FromJwtExpirationMinutes` (both in `ApiEndpointsTests`). The second uses `WithWebHostBuilder` with `Jwt:ExpirationMinutes = 15`.
  - Mutation checks: putting back the hard-coded `+60 min` and dropping the actor argument each fail their test.
- **2026-10-13 — PR B implemented** on `fix/registration-race-and-email-normalization`. No change to the approach. Details:
  - Subdomain comparisons keep `ToLower()`. Only the email comparisons became plain `==`, as the plan said: no migration normalizes subdomains, and rows stored before #136 may not be lowercase.
  - The existing test `TenantRegistrationValidatorTests.Validate_ShouldTrimAndLowercaseSubdomain_AndTrimCompanyNameAndEmail` asserted the old trim-only email. It was renamed `..._ShouldTrimAndLowercaseSubdomainAndEmail_AndTrimCompanyName` and now expects a lowercase email.
  - Race tests: the competing row is committed from `DbTransactionInterceptor.TransactionStartingAsync`, as planned. The "not a conflict" case uses a `SaveChangesInterceptor` that throws a `DbUpdateException` instead. A throw in `TransactionStartingAsync` would fire at `BeginTransactionAsync`, which is outside the code under test.
  - Migration check on a throwaway `postgres:16` container:
    - Mixed-case users and invites were lowercased.
    - With two users differing only in case, the pre-migration duplicate query found them, and the migration failed with `23505` and rolled back. No rows changed and it was not recorded as applied. Because both users were in the same tenant, the violated index was `IX_Users_TenantId_Email` rather than `IX_Users_Email`, so the migration comment and the docs mention both.
  - Mutation checks:
    - Disabling the `DbUpdateException` mapping fails both race tests.
    - Making `EmailAddress.Normalize` trim-only fails five tests: the validator test, the email race test, both new normalization integration tests, and the #142 duplicate-email test.
