---
workflowType: story-detail
status: done
storyId: S-E1-04
epic: E1
derivedFrom:
  - '_bmad-output/planning-artifacts/epics-and-stories.md'
  - '_bmad-output/planning-artifacts/prd.md'
  - '_bmad-output/planning-artifacts/architecture.md'
  - '_bmad-output/planning-artifacts/ux-design-specification.md'
inputDocuments:
  - '_bmad-output/planning-artifacts/epics-and-stories.md'
  - '_bmad-output/planning-artifacts/architecture.md'
  - '_bmad-output/planning-artifacts/prd.md'
  - '_bmad-output/planning-artifacts/ux-design-specification.md'
  - '_bmad-output/stories/S-E1-03-assign-change-user-roles.md'
---

# S-E1-04 — Enforce RBAC on restricted operations (FR4)

Status: done

<!-- Ultimate context engine analysis completed — comprehensive developer guide created. -->

## Story

**As the** system, **I must** enforce RBAC on every restricted operation and return an explicit denial for unauthorized access, **so that** data access matches the RBAC matrix.

**Traceability:** FR4 · PRD § Authentication & User Management; PRD **RBAC Matrix**; Epic E1 outcome (“RBAC is enforced on every protected capability”).

---

## Acceptance criteria

1. **Server-side only:** Authorization for restricted capabilities is enforced in the API (ASP.NET authorization), not by hiding UI alone. **Authenticated but wrong-role** calls receive **403 Forbidden** with the existing JSON shape (`AuthErrorResponse` via `JsonForbiddenAuthorizationMiddlewareResultHandler` — same message via `AuthMessages.Forbidden`, as in `Users_IC_token_receives_403_with_json_message`). **Anonymous** calls (no bearer token) receive **401 Unauthorized** (standard JWT bearer challenge), not the JSON 403 body — integration tests assert this for the new stub routes.
2. **Spot-check denials (epics & PRD intent):** Integration tests prove **403** for:
   - **IC** calling an **org-wide timesheets** read capability (matrix: only **Admin** + **Finance** — FR10 visibility).
   - **IC** (and **Manager**) calling an **employee billing rates** read capability (matrix: **Admin** + **Finance** only).
   - **Finance** calling **create client** (matrix: **Admin** only for create/edit clients).
   - **Manager** calling a **Finance-only invoice** operation (matrix: **Generate & export invoices** = **Admin** + **Finance** only).
3. **Spot-check allows:** Same tests (or paired tests) prove **200** (or **204** if you standardize empty stubs) when a user with a **permitted** role calls the same endpoint (e.g. Finance on org-timesheets stub, Admin on create-client stub).
4. **Consistency with existing auth:** All new protected routes run after `UseAuthentication`, `RequireActiveUserMiddleware`, and `UseAuthorization` (order unchanged in `Program.cs`). JWT role claim remains `ClaimTypes.Role` = `user.Role.ToString()` (`IC`, `Admin`, `Manager`, `Finance`) — see `JwtTokenService.CreateAccessToken`.
5. **Pattern for future epics:** New domain controllers **must** use explicit `[Authorize]` (roles and/or named policies). Document in Dev Notes where the matrix is mapped (constants or `AuthorizationOptions` policies) so E2–E11 do not invent ad-hoc role strings.
6. **No regression:** Existing flows stay green: login, `/api/auth/me`, admin user CRUD, role patch (`UsersAdminTests` + auth tests).

---

## Out of scope (explicit)

- **Full domain behavior** for timesheets, clients, invoices (payloads, persistence, validation) — those stories fill business logic; this story only **gates** routes that represent matrix rows.
- **UI role hiding** — may mirror server rules for UX; not sufficient alone (already ADR-02).
- **Hangfire / background job authorization** — architecture mentions Admin-only dashboard later; defer unless already present.
- **URL versioning `/api/v1/`** — architecture mentions it; current codebase uses `/api/...` — stay consistent with existing controllers until a dedicated versioning story.

---

## Tasks / Subtasks

- [x] Add minimal **placeholder endpoints** (or the first slice of real controllers) whose **only** stable contract for this story is **who may call them**. Prefer REST-shaped paths that future epics can adopt without renaming (suggested mapping below).
- [x] Register **named authorization policies** *or* consistent `[Authorize(Roles = ...)]` using `nameof(AppRole.X)` / documented comma-separated role lists — pick one style and apply everywhere new in this story.
- [x] Add **`RbacEnforcementTests.cs`** (or extend an existing suite) using the same `WebApplicationFactory<Program>` + unique `Database:InMemoryName` + test JWT signing key pattern as `UsersAdminTests`.
- [x] Seed or create users per role (IC, Manager, Finance, Admin) in tests — mirror patterns from `UsersAdminTests` (admin creates users, login per role).
- [x] Run `dotnet test` for `C2E.Api.Tests`; fix any ordering/middleware regressions.

### Review Findings

- [x] [Review][Decision] AC1 vs HTTP semantics for anonymous callers — **Resolved (2026-04-06):** AC1 updated; anonymous → **401**, wrong-role → **403** JSON; `RbacEnforcementTests.Rbac_stub_routes_return_401_when_anonymous` added.
- [x] [Review][Patch] Duplicate forbidden message string — **Resolved:** `AuthMessages.Forbidden`; handler + `RbacEnforcementTests` + `UsersAdminTests` reference it.
- [x] [Review][Defer] `AdminAndFinance` constant couples three distinct matrix rows — acceptable MVP; if one row’s roles diverge later, split constants or policies [`api/Authorization/RbacRoleSets.cs:12`] — deferred, pre-existing design choice.
- [x] [Review][Defer] `web/dist/` build output tracked in repo — increases diff noise and merge churn; consider gitignoring `dist` and building in CI — deferred, pre-existing.

### Suggested route ↔ matrix mapping (implementer may adjust paths if epics already chose names)

| Matrix idea | Roles allowed | Suggested stub (MVP) |
|-------------|---------------|----------------------|
| Org-wide timesheets (FR10) | Admin, Finance | `GET /api/timesheets/organization` → `204` or `Ok([])` |
| Employee billing rates | Admin, Finance | `GET /api/clients/billing-rates` or `GET /api/clients/sample/billing-rate` → minimal payload |
| Create client | Admin only | `POST /api/clients` → `201` with minimal body or `204` if no DB entity yet |
| Invoice generate (Finance-only flow) | Admin, Finance | `POST /api/invoices/generate` → `204` |

**Merge strategy:** When S-E2-xx / S-E8-xx / S-E10-xx land, **replace stub implementations** with real handlers but **keep** the same `[Authorize]` attributes unless the PRD matrix changes.

---

## Dev Notes

### Current implementation snapshot

- **Layer 1 (route):** `[Authorize(Roles = nameof(AppRole.Admin))]` on `UsersController`; `[Authorize]` on authenticated `AuthController` actions.
- **403 JSON:** `JsonForbiddenAuthorizationMiddlewareResultHandler` writes `AuthErrorResponse` with `AuthMessages.Forbidden` (wrong-role only; anonymous gets 401 challenge).
- **Active user gate:** `RequireActiveUserMiddleware` after authentication.
- **No** standalone `RbacPolicyMiddleware.cs` yet — architecture diagram references it; **ASP.NET Core’s built-in authorization middleware + policies** satisfies ADR-02 layer 1 if every endpoint is attributed. If you add a thin `RbacPolicyMiddleware`, document why (e.g. centralized logging) and avoid duplicating role checks.

### Architecture compliance

- **ADR-02:** Two layers — (1) role/capability at route/policy, (2) resource ownership in services when IDs are scoped (e.g. “my timesheet” vs “org”). This story focuses on **(1)**; layer **(2)** applies when handlers query by user id (later epics).
- **Anti-pattern:** ❌ Controller without `[Authorize]` that relies on SPA navigation — forbidden.

### File structure (expected touches)

| Area | Paths |
|------|--------|
| API — new stubs | `api/Controllers/TimesheetsController.cs`, `ClientsController.cs`, `InvoicesController.cs` (or fewer files if grouped — but keep concerns separable) |
| API — policies (optional) | `api/Authorization/RbacPolicies.cs` + `Program.cs` `AddAuthorization` configuration |
| Tests | `tests/C2E.Api.Tests/RbacEnforcementTests.cs` |

### UX

- No new screens required. If the SPA calls stub URLs before domain UI exists, return errors only through API — no change to `AdminUsers` flows.

### Testing requirements

- **Forbidden:** Assert `StatusCode == Forbidden` and deserialize `AuthErrorDto` / `AuthErrorResponse` message matches `AuthMessages.Forbidden` (shared with `UsersAdminTests`).
- **Anonymous:** Assert `401 Unauthorized` on stub routes when `Authorization` is unset (JWT bearer default).
- **Happy path:** Bearer token for allowed role; assert success status.
- Use **distinct** in-memory DB name per factory instance (see `UsersAdminTests.Factory`).

### References

- [Source: `_bmad-output/planning-artifacts/epics-and-stories.md` — Epic E1, S-E1-04, S-E1-04 acceptance criteria]
- [Source: `_bmad-output/planning-artifacts/prd.md` — FR4, RBAC Matrix table]
- [Source: `_bmad-output/planning-artifacts/architecture.md` — ADR-02 RBAC, cross-cutting RBAC enforcement, `JsonForbidden` pattern]

---

## Previous story intelligence (S-E1-03)

- Role strings in JWT match `AppRole` enum names exactly (`Admin`, `IC`, `Manager`, `Finance`).
- Admin-only user APIs: `[Authorize(Roles = nameof(AppRole.Admin))]` on controller class.
- Integration tests: `WebApplicationFactory<Program>`, `Database:InMemoryName` unique GUID, `Jwt:SigningKey` test 32+ chars, `LoginResponseDto` / `AuthErrorDto` in tests.
- **403** body shape is stable — new RBAC tests should assert the same message for consistency.

---

## Latest tech notes (MVP stack)

- **.NET 9** + `Microsoft.AspNetCore.Authorization` policy and role handlers.
- `IAuthorizationMiddlewareResultHandler` is already used for JSON 403 — keep behavior for failed authorization (not only `UsersController`).

---

## Project context reference

- No `project-context.md` in repo; use architecture + PRD + this file.

---

## Dev Agent Record

### Agent Model Used

Composer (Cursor agent)

### Debug Log References

_(none)_

### Completion Notes List

- Stub routes: `GET /api/timesheets/organization`, `GET /api/clients/billing-rates`, `POST /api/clients`, `POST /api/invoices/generate` with `[Authorize(Roles = RbacRoleSets.*)]` using `nameof(AppRole)`-derived comma lists in `api/Authorization/RbacRoleSets.cs` (AC5 mapping for E2–E11).
- Integration coverage in `RbacEnforcementTests.cs`: IC/Manager/Finance/Admin denials and allows per AC2–AC3; 403 body uses `AuthMessages.Forbidden`; anonymous stub calls assert 401.
- `AuthMessages.Forbidden` + handler + admin/RBAC tests (25 tests after new fact).

### File List

- `api/AuthMessages.cs`
- `api/Authorization/RbacRoleSets.cs`
- `api/Authorization/JsonForbiddenAuthorizationMiddlewareResultHandler.cs`
- `api/Controllers/TimesheetsController.cs`
- `api/Controllers/ClientsController.cs`
- `api/Controllers/InvoicesController.cs`
- `tests/C2E.Api.Tests/RbacEnforcementTests.cs`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`

---

## Change Log

- 2026-04-06 — Story S-E1-04 created (create-story workflow); status **ready-for-dev**.
- 2026-04-06 — Implemented RBAC stubs, `RbacRoleSets`, `RbacEnforcementTests`; sprint status **review**.
- 2026-04-06 — Code review: AC1 clarified (401 vs 403), `AuthMessages.Forbidden`, anonymous stub-route tests; status **done**.

---

## Definition of done (story-level)

- [x] All acceptance criteria (1–6) satisfied with automated tests for AC2–AC3 and AC6 via CI.
- [x] Documented mapping from matrix rows to policies/roles for future stories.
- [x] Wrong-role access receives explicit **403** JSON on new stubs; anonymous receives **401** per HTTP/JWT conventions.
