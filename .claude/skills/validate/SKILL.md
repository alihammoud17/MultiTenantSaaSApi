---
name: validate
description: Run this repo's required local validation for the current change (.NET API, BillingService, or docs-only) and report exactly which commands ran and their results, as AGENTS.md requires.
disable-model-invocation: true
argument-hint: "[dotnet|billing|docs|all]"
---

# Validate the current change

Run the validation that `AGENTS.md` and `BillingService/AGENTS.md` require for the current change, then report it. Run every command from the repository root unless a step says otherwise. Do not run `scripts/dev.sh test`: it hard-requires `/tmp/dotnet-tools/dotnet-ef` and skips the BillingService typecheck. Run the steps below directly instead.

## 1. Decide the scope

If `$ARGUMENTS` is `dotnet`, `billing`, `docs`, or `all`, use it. Otherwise derive it from the changed files:

```bash
git status --porcelain
git diff --name-only HEAD
```

On a branch other than `master`, also include `git diff --name-only master...HEAD`.

- Any change under `Presentation/`, `Application/`, `Domain/`, `Infrastructure/`, `Tests/`, `*.sln`, `*.csproj`, or `appsettings*.json` → **dotnet**
- Any change under `BillingService/` other than `*.md` → **billing**
- Any of `docs/contracts.md`, `BillingService/tests/billingCallbackContract.test.ts`, `Tests/Integration/CrossServiceBillingContractTests.cs` → **dotnet + billing** (the producer and consumer suites must both run)
- Only `*.md` files changed → **docs**
- Nothing changed → say so and stop

Every scope also runs the docs check in step 4.

## 2. dotnet scope

```bash
dotnet --info | head -20
dotnet restore
dotnet build --no-restore
dotnet test --no-build --verbosity normal
```

`AGENTS.md` also lists `/tmp/dotnet-tools/dotnet-ef --version`. Run it only if that file exists; otherwise try `dotnet ef --version`, and if neither works, report it as not run. Only migration work actually needs dotnet-ef.

From the `dotnet test` output, record total, passed, failed, and **skipped** counts. Container-backed tests (`Tests/Containers/*Fixture.cs`) skip when Docker is unreachable. If any were skipped, say so explicitly: the Postgres/Redis-backed paths were not exercised.

## 3. billing scope

Run from `BillingService/`:

```bash
npm ci
npm run build
npm run typecheck
npm test
```

## 4. Docs check (always)

```bash
git diff --check
```

For each changed `*.md` file, verify that any file paths, scripts, and commands it references still exist in the repo. If runtime code or config changed, check whether the affected docs (`README.md`, `docs/architecture.md`, `docs/operations.md`, `docs/contracts.md`, `docs/decisions.md`, `docs/CHANGELOG.md`, `BillingService/README.md`) were updated in the same change, and list any that look stale. Do not edit them; just report.

## Failure handling

Don't stop at the first failure. Keep running the remaining independent steps (for example, still run the BillingService steps when `dotnet test` fails) so the report is complete. A failed `restore`/`build` makes the steps that depend on it "not run", not "failed". Do not try to fix failures unless the user asks.

## 5. Report

End with this table and nothing hidden behind it:

| Command | Result | Notes |
|---|---|---|
| `dotnet build --no-restore` | ✅ passed / ❌ failed / ⏭ not run | warnings, error summary, or the reason it was not run |
| … | … | … |

Then state:
- the scope you chose and why
- test counts (passed / failed / skipped) per suite
- for each failure, the failing test names and the key error lines, quoted verbatim
- anything required that was not run, and why
