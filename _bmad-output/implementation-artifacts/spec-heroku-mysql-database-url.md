---
title: 'Connect Heroku MySQL via DATABASE_URL'
type: 'feature'
created: '2026-04-15'
status: 'done'
baseline_commit: '88ed1d852035729952241ca9d97e8b2fe62c89d8'
context:
  - '_bmad-output/project-context.md'
  - 'README.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The API currently resolves Heroku `DATABASE_URL` only as Postgres and wires EF Core through Npgsql, so a Heroku MySQL add-on URL cannot be consumed for runtime startup or design-time migrations.

**Approach:** Add first-class MySQL connectivity alongside the current in-memory behavior by parsing Heroku MySQL URLs, selecting the EF provider dynamically, and aligning startup, design-time factory, dependencies, and tests with the new provider path.

## Boundaries & Constraints

**Always:** Keep in-memory behavior unchanged for tests/local defaults; keep `DATABASE_URL` precedence over `ConnectionStrings:DefaultConnection`; enforce SSL/TLS options in generated MySQL connection strings for Heroku; preserve existing controller/API behavior (this change is infrastructure only).

**Ask First:** Whether to fully remove Postgres support vs keep dual-provider support; whether existing migrations should be replaced/reset for MySQL compatibility; whether to introduce a dedicated env var (for example `JAWSDB_URL`) in addition to `DATABASE_URL`.

**Never:** Do not silently alter domain models or API contracts; do not store credentials in committed config files; do not bypass integration tests by changing test setup away from in-memory DB.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Heroku MySQL URL | `DATABASE_URL=mysql://user:pass@host:3306/dbname?...` | Connectivity resolves to MySQL provider and valid MySQL connection string with SSL mode | If URL cannot be parsed, throw startup exception with actionable message |
| Non-MySQL URL with MySQL-only mode | `DATABASE_URL=postgres://...` while only MySQL provider is configured | Startup fails fast before serving requests | Return clear invalid provider/scheme message in logs/exception |
| Local explicit connection string | Empty `DATABASE_URL`, non-empty `ConnectionStrings:DefaultConnection` for MySQL | Connectivity resolves to MySQL provider and app starts with migrations applied | If connection string invalid, fail during DB initialization with provider exception |
| Test/in-memory precedence | `Database:InMemoryName` provided in test host | App continues using in-memory DB regardless of env connection strings | No error; preserve current integration test behavior |

</frozen-after-approval>

## Code Map

- `api/C2E.Api.csproj` -- add EF Core MySQL provider package and keep required design/runtime packages aligned.
- `api/Data/DatabaseConnectivity.cs` -- extend connectivity model and provider resolution for MySQL URL/connection string flows.
- `api/Data/HerokuDatabaseUrl.cs` (or replacement parser helper) -- parse Heroku MySQL URI into EF-compatible MySQL connection string.
- `api/Data/DbContextRegistration.cs` -- register `UseMySql` when MySQL provider is selected; keep in-memory path.
- `api/Data/AppDbContextFactory.cs` -- align design-time provider selection with runtime rules for migrations tooling.
- `api/Program.cs` -- keep DB initialization branching aligned with provider set (in-memory ensure created vs relational migrate).
- `tests/C2E.Api.Tests/HerokuDatabaseUrlTests.cs` -- replace/add parser tests for MySQL URL conversion and invalid scheme handling.
- `README.md` -- update deployment/config docs from Postgres-specific wording to Heroku MySQL path.

## Tasks & Acceptance

**Execution:**
- [x] `api/C2E.Api.csproj` -- add MySQL EF Core provider dependency and remove provider mismatch risk -- enables runtime/design-time provider APIs.
- [x] `api/Data/DatabaseConnectivity.cs` -- add MySQL database kind and update resolution output fields -- centralizes provider decision logic.
- [x] `api/Data/HerokuDatabaseUrl.cs` (or new helper in `api/Data/`) -- implement MySQL URL parsing with URL-decoding and SSL defaults -- converts Heroku URL into usable connection string.
- [x] `api/Data/DbContextRegistration.cs` -- branch to `UseMySql` for MySQL connectivity while keeping in-memory branch unchanged -- wires EF correctly at startup.
- [x] `api/Data/AppDbContextFactory.cs` -- mirror runtime provider selection for tooling and migrations -- prevents migration provider drift.
- [x] `api/Program.cs` -- confirm relational startup path remains `MigrateAsync` and in-memory remains `EnsureCreatedAsync` -- preserves startup semantics.
- [x] `tests/C2E.Api.Tests/HerokuDatabaseUrlTests.cs` -- validate MySQL parsing happy-path and failure-path -- guards against malformed URL regressions.
- [x] `README.md` -- document required Heroku env setup and local configuration expectations for MySQL -- keeps deployment instructions accurate.

**Acceptance Criteria:**
- Given a valid Heroku MySQL `DATABASE_URL`, when the API starts, then `AppDbContext` is configured with MySQL provider and relational migrations execute successfully.
- Given malformed or unsupported `DATABASE_URL` scheme, when startup resolves DB connectivity, then startup fails with a clear provider parsing error.
- Given `Database:InMemoryName` is set in tests, when integration tests run, then in-memory provider remains selected and existing test behavior is preserved.
- Given no `DATABASE_URL` but a valid MySQL `ConnectionStrings:DefaultConnection`, when the API starts, then the app uses MySQL without requiring Heroku URL parsing.

## Spec Change Log

## Verification

**Commands:**
- `dotnet restore C2E.sln` -- expected: restore succeeds with MySQL provider package resolved
- `dotnet build C2E.sln` -- expected: solution builds without provider API errors
- `dotnet test tests/C2E.Api.Tests/C2E.Api.Tests.csproj` -- expected: tests pass including updated DB URL parser tests

## Suggested Review Order

**Provider Selection Runtime Flow**

- Start here to verify MySQL vs in-memory routing precedence and fallback.
  [`DatabaseConnectivity.cs:14`](../../api/Data/DatabaseConnectivity.cs#L14)

- Confirm DI registration applies provider-specific EF configuration for resolved kind.
  [`DbContextRegistration.cs:17`](../../api/Data/DbContextRegistration.cs#L17)

**Heroku URL Parsing and Hardening**

- Validate mysql URL parsing, strict scheme checks, and actionable parse failures.
  [`HerokuDatabaseUrl.cs:9`](../../api/Data/HerokuDatabaseUrl.cs#L9)

- Ensure connection string uses connector-safe builder semantics and TLS preference.
  [`HerokuDatabaseUrl.cs:45`](../../api/Data/HerokuDatabaseUrl.cs#L45)

**Design-Time and Migration Alignment**

- Verify design-time factory mirrors runtime selection and supports EF tooling.
  [`AppDbContextFactory.cs:16`](../../api/Data/AppDbContextFactory.cs#L16)

- Review provider swap dependency that enables MySQL EF runtime/design-time integration.
  [`C2E.Api.csproj:18`](../../api/C2E.Api.csproj#L18)

- Inspect regenerated MySQL baseline migration replacing previous provider-specific chain.
  [`20260415151428_InitialCreate.cs:9`](../../api/Data/Migrations/20260415151428_InitialCreate.cs#L9)

**Validation and Documentation**

- Check parser edge-case test coverage for malformed URLs and invalid credentials.
  [`HerokuDatabaseUrlTests.cs:44`](../../tests/C2E.Api.Tests/HerokuDatabaseUrlTests.cs#L44)

- Confirm deployment guidance now reflects Heroku MySQL configuration expectations.
  [`README.md:36`](../../README.md#L36)

