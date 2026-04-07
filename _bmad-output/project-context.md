---
project_name: 'C2E-MIS-321-GP2'
user_name: 'C2E'
date: '2026-04-07'
sections_completed:
  - technology_stack
  - language_rules
  - framework_rules
  - testing_rules
  - quality_rules
  - workflow_rules
  - anti_patterns
status: complete
rule_count: 42
optimized_for_llm: true
---

# Project Context for AI Agents

_This file contains critical rules and patterns that AI agents must follow when implementing code in this project. Focus on unobvious details that agents might otherwise miss._

---

## Technology Stack & Versions

| Area | Tech | Notes |
|------|------|--------|
| Backend | .NET **9** (`net9.0`) | `Nullable` + `ImplicitUsings` enabled |
| API | ASP.NET Core controllers, JWT Bearer | See `api/C2E.Api.csproj` for exact package versions |
| Data | EF Core **InMemory** + **Npgsql** `9.0.0` | **Tests / default local:** `Database:InMemoryName` → InMemory + `EnsureCreated`. **Heroku:** env `DATABASE_URL` (`postgres://…`) → Postgres + `Migrate` on startup. **Local Postgres:** non-empty `ConnectionStrings:DefaultConnection`. See `DatabaseConnectivity`, `HerokuDatabaseUrl`, `AppDbContextFactory` (design-time migrations). |
| Identity | `PasswordHasher<AppUser>`, custom `AppUser` / `AppRole` | Roles map to JWT `ClaimTypes.Role` |
| API docs | `Microsoft.AspNetCore.OpenApi` `9.0.8` | OpenAPI mapped in Development |
| Frontend | React `^19.2.4`, Vite `^8.x`, TypeScript `~5.9.3` | ES modules; see `web/package.json` |
| Routing | `react-router-dom` `^7.14.0` | Client-only `BrowserRouter` |
| Lint | ESLint 9 flat config + `typescript-eslint` recommended | `web/eslint.config.js` |
| Tests | xUnit `2.9.2`, `WebApplicationFactory<Program>` | In-memory DB + JWT settings per factory |

**Local dev URLs:** API default `http://localhost:5028` (see launch settings); Vite `http://localhost:5173`. CORS defaults to `http://localhost:5173` if `Cors:Origins` empty.

**JWT:** Startup fails if `Jwt:SigningKey` missing or < 32 chars, or `AccessTokenMinutes` < 1. Dev key lives in `api/appsettings.Development.json` (do not reuse in prod).

---

## Critical Implementation Rules

### Language-Specific Rules

**C# / .NET**

- Use `Guid` user id from JWT: `ClaimTypes.NameIdentifier` first, else `ClaimTypes.Name` — same pattern as `TimesheetsController.TryGetUserId` and `RequireActiveUserMiddleware`.
- Anonymous endpoints: mark with `[AllowAnonymous]` so `RequireActiveUserMiddleware` skips the active-user DB check.
- API errors returned as JSON with `message` (see `AuthErrorResponse` / `AuthMessages`); keep login failure message generic (`Invalid email or password.`).
- `weekStart` and line `workDate`: **`yyyy-MM-dd`** only; **`weekStart` must be Monday** (server validates).
- Timesheet upsert: hours **> 0 and ≤ 24**, **0.25 increments**; `client` / `project` / `task` trimmed required strings; **duplicate** (same workDate+client+project+task) in one payload is **invalid**.
- `workDate` must fall in **[weekStart, weekStart+7 days)**.

**TypeScript**

- **`strict: true`** (plus `noUnusedLocals`, `noUnusedParameters`, etc. in `tsconfig.app.json`) — no sloppy `any` without justification.
- API base: `import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5028'`.
- Validate JSON from API when shapes matter (pattern: `api.ts` `assertTimesheetLine`, `me()` field checks); do not assume `res.json()` shape.
- Session: token **in memory only** (refresh clears) — don’t assume `localStorage` unless product explicitly changes.

### Framework-Specific Rules

**ASP.NET Core**

- Register `JsonForbiddenAuthorizationMiddlewareResultHandler` for consistent forbidden JSON if expanding auth.
- Middleware order: `UseAuthentication` → `RequireActiveUserMiddleware` → `UseAuthorization` → `MapControllers` (see `Program.cs`).
- RBAC: use **`RbacRoleSets`** constants in `[Authorize(Roles = ...)]` — e.g. `AdminAndFinance` = `Admin,Finance`, `AdminOnly` = `Admin`.
- `TimesheetsController`: org route `GET api/timesheets/organization` is **Admin + Finance**; per-user week is **`[Authorize]`** only.

**React**

- Role-gated UX: mirror server rules in routes (e.g. `AdminUsersRoute` requires `Admin`); **never rely on UI alone** for security.
- Use existing shell/card/hint CSS classes in `App.css` / `index.css` for consistency.

### Testing Rules

- New API behavior → add **`WebApplicationFactory<Program>`** tests under `tests/C2E.Api.Tests/`.
- Each test run must use a **unique** `Database:InMemoryName` (GUID suffix) and a **valid test** `Jwt:SigningKey` (≥ 32 chars) via `UseSetting` — copy `AuthLoginTests` / `TimesheetWeekTests` factory pattern.
- Seed admin credentials via `Seed:DevUserEmail` / `Seed:DevUserPassword` in factory when tests need a known login.
- Assert **status codes and JSON bodies** (e.g. `AuthErrorDto.Message` for auth failures).
- Timesheet isolation: non-owner accessing another user’s week → **403** (`Forbid()`), not leaked data.

### Code Quality & Style Rules

- Match existing layout: `api/Controllers`, `api/Dtos`, `api/Models`, `api/Services`, `api/Middleware`; `web/src/pages`, `web/src/api.ts`.
- C# naming: PascalCase types, private helpers clear; TS: components PascalCase, hooks/callbacks as in `App.tsx`.
- ESLint: run `npm run lint` in `web` before PR; backend: `dotnet build` / `dotnet test`.

### Development Workflow Rules

- Solution entry: `C2E.sln`; run API: `dotnet run --project api/C2E.Api.csproj`; web: `cd web && npm run dev`.
- Copy `web/.env.example` to `.env` if you need a non-default `VITE_API_BASE_URL`.
- BMad artifacts: planning under `_bmad-output/planning-artifacts`, this file under `_bmad-output/`.

### Critical Don't-Miss Rules

- **Deactivated users:** middleware returns **401** with same generic message as bad password — don’t leak account state.
- **Finance role:** defined in matrix (`RbacRoleSets.AdminAndFinance`); add Finance-only endpoints using that constant, not ad-hoc role strings.
- Timesheet PUT is **upsert**: omitted lines are **not** deleted implicitly — document if adding delete semantics later.
- **HTTPS redirection** is on; integration tests use `CreateClient()` (handles redirect/base address).
- Do not commit real production JWT keys or passwords; use user secrets / env in real deployments.
- **Postgres / Heroku:** `DATABASE_URL` is parsed in `HerokuDatabaseUrl`; migrations live in `api/Data/Migrations`. New migrations: `dotnet ef migrations add <Name> --project api/C2E.Api.csproj --startup-project api/C2E.Api.csproj --output-dir Data/Migrations`. On Postgres, startup runs **`MigrateAsync`**; tests keep **`Database:InMemoryName`** and use **`EnsureCreatedAsync`**.

---

## Usage Guidelines

**For AI Agents**

- Read this file before implementing any feature.
- Follow server-side RBAC and validation rules; extend `RbacRoleSets` and tests together.
- Prefer extending `web/src/api.ts` for HTTP and keeping components thin.

**For Humans**

- Update this file when the stack, auth model, or timesheet rules change.
- Trim rules that become obvious over time; keep this document dense and scannable.

_Last updated: 2026-04-07_
