---
workflowType: story-detail
status: done
storyId: S-E1-03
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
  - '_bmad-output/stories/S-E1-02-admin-user-account-lifecycle.md'
---

# S-E1-03 — Admin: assign and change user roles

Status: done

<!-- Ultimate context engine analysis completed — comprehensive developer guide created. -->

## Story

**As an** admin, **I want** to assign and change roles (Admin / Manager / Finance / IC) for any user, **so that** permissions match job function.

**Traceability:** FR3 · PRD § Authentication & User Management; RBAC matrix row “Manage users & roles” (Admin only).

---

## Acceptance criteria

1. **Assign / change role:** Admin can set a user’s role to any of `Admin`, `Manager`, `Finance`, `IC` after the account exists. Changes persist in the user store (`AppUser.Role`).
2. **Authorization:** Only **Admin** may change roles (same as user management). Non-admin receives **403** with the existing JSON error shape (`AuthErrorResponse` / `JsonForbiddenAuthorizationMiddlewareResultHandler`).
3. **Validation:** Invalid role strings return **400** with a clear message (existing: `"Invalid role. Use IC, Admin, Manager, or Finance."`).
4. **Last-admin safety:** Cannot demote or deactivate the **last active** `Admin` (existing **409 Conflict** messages — keep consistent).
5. **Effective permissions after change:** A user who logs in **after** a role change receives JWT + `GET /api/auth/me` reflecting the **new** role (no stale role in identity for new sessions).
6. **Admin UI:** On `/admin/users`, admin can **edit** a user and change role via control that lists all four roles (not free text). Non-admin must not reach this UI (client gating + server enforcement).
7. **Self-service demotion:** If the logged-in admin saves their **own** role to non-Admin, SPA should **sign out** or otherwise prevent a broken admin session (existing: `saveEdit` calls `onSignOut()` when `id === profile.id && updated.role !== 'Admin'`).
8. **Tests:** Integration tests cover role patch happy path, invalid role, non-admin forbidden, last-admin demote blocked, and `/me` after login reflects patched role.

---

## Out of scope (explicit)

- **Full RBAC on domain routes** (timesheets, clients, invoices, etc.) — **S-E1-04** (FR4).
- **Optional role at user creation** — MVP may keep default `IC` on `POST /api/users`; FR3 is satisfied by assign/change after create. Add `role` on create only if product asks; not required for FR3 closure.

---

## Developer context & guardrails

### Current implementation snapshot (verify, do not blindly re-build)

Much of FR3 was implemented alongside **S-E1-02**. Treat this story as **verification + gap fill**, not greenfield.

| Area | Location | Expected behavior |
|------|----------|-------------------|
| API | `api/Controllers/UsersController.cs` | `PATCH /api/users/{id}` accepts `role` in body; validates enum names; last-admin checks for demote + deactivate |
| DTO | `api/Dtos/UpdateUserRequest.cs` | `Role` optional string |
| SPA | `web/src/pages/AdminUsers.tsx` | `APP_ROLES`, `editRole` `<select>`, `patchUser` sends `role` when changed; self-demotion signs out |
| Tests | `tests/C2E.Api.Tests/UsersAdminTests.cs` | `Users_patch_role_IC_to_Manager_then_Finance`, invalid role, IC 403 on patch, last admin demote, `Me_after_login_reflects_patched_role` |

**If all ACs above already pass in CI:** update story status to **done** via dev workflow, note “implemented under S-E1-02” in completion notes, and avoid duplicate endpoints.

### Architecture compliance

- **ADR-02:** Role change is a **privileged** operation — must remain **server-side** on `UsersController` (Admin role only). Never UI-only.
- **JWT:** Role claim must stay in sync with DB for **new** tokens; existing tokens remain valid until expiry — acceptable for MVP; S-E1-02 already documents inactive-user middleware for account state.
- **API shape:** Stay consistent with `AuthController` / `api.ts` error handling; no new error DTO per endpoint.

### File structure (touch only if gaps found)

| Area | Paths |
|------|--------|
| API | `api/Controllers/UsersController.cs`, `api/Dtos/UpdateUserRequest.cs`, `api/Models/AppRole.cs` |
| Web | `web/src/pages/AdminUsers.tsx`, `web/src/api.ts` (`patchUser`) |
| Tests | `tests/C2E.Api.Tests/UsersAdminTests.cs` |

### UX

- Toasts ~4s on success/failure; role change is **not** an “irreversible destructive” pattern requiring the same inline confirm as deactivate — optional copy toast e.g. “Role updated” is enough. [Source: `_bmad-output/planning-artifacts/ux-design-specification.md` — confirm vs interrupt]
- Role-aware nav: after demoting self from Admin, user must not remain on `/admin/users` without access — **sign-out** path already implemented.

### Testing requirements

- `dotnet test` for `C2E.Api.Tests` — role-related cases must stay green.
- `npm run build` / `npm run lint` for web if SPA changes.
- Add tests **only** if coverage is missing for an AC (e.g. promote IC → Admin, or Admin → IC when another Admin exists).

---

## Previous story intelligence (S-E1-02)

- User APIs live on **`/api/users`** with `[Authorize(Roles = Admin)]`.
- Passwords: `PasswordHasher<AppUser>`; email normalized `Trim().ToLowerInvariant()`.
- **403** JSON for forbidden; **401** with `Invalid email or password.` for inactive/stale session on protected routes via `RequireActiveUserMiddleware`.
- Seeded dev user is Admin; new users default **IC**.
- Integration tests use **unique** in-memory DB name per factory (`Database:InMemoryName`).

---

## Latest tech notes (MVP stack)

- **.NET 9** + ASP.NET Core JWT bearer + role claims; **React + Vite + TS** client.
- No requirement to introduce FluentValidation or `/api/v1` for this story alone.

---

## Project context reference

- No `project-context.md` in repo yet; use this file + architecture + PRD for agent rules.

---

## Tasks / Subtasks

- [x] Map each AC (1–8) to a test or manual check; confirm all pass on current `main`/branch.
- [x] If any AC fails, implement minimal fix in the files listed above; extend tests.
- [x] Optional: add `role` to `CreateUserRequest` + create form if product wants initial role ≠ IC (out of scope unless requested). — **Skipped** per story Out of scope; not requested.
- [x] Run `dotnet test`, `npm run build` (and lint if configured).

### Review Findings

- [x] [Review][Defer] Last-admin protection is not concurrency-safe (TOCTOU between `AnyAsync` and `SaveChanges`) [`api/Controllers/UsersController.cs:85-101`] — deferred, pre-existing pattern for MVP; address with stricter isolation or serialized admin ops if required later.

- [x] [Review][Patch] Add integration test for case-insensitive role patch (e.g. `manager`, `FINANCE`) [`tests/C2E.Api.Tests/UsersAdminTests.cs`]

---

## Dev Agent Record

### Agent Model Used

Composer (Cursor agent)

### Debug Log References

_(none)_

### Completion Notes List

- Verified FR3 / AC1–8 against existing S-E1-02 implementation: `UsersController.Patch` role handling, `AdminUsers` role `<select>`, self-demotion `onSignOut`, `AdminUsersRoute` gating in `App.tsx`.
- `dotnet test` (`C2E.Api.Tests`: 20), `npm run build` / `npm run lint` for `web` — green after review.
- Code review (2026-04-06): added `Users_patch_role_case_insensitive_returns_canonical_names` (`manager` → Manager, `FINANCE` → Finance).
- Extended tests: IC role-patch 403 now asserts `AuthErrorDto` message (AC2); added `Users_patch_promote_IC_to_Admin_when_second_admin_exists` (AC1 Admin assignment).
- UX: toast shows “Role updated” when only `role` is patched (optional copy from UX spec).

### File List

- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/stories/S-E1-03-assign-change-user-roles.md`
- `tests/C2E.Api.Tests/UsersAdminTests.cs`
- `web/src/pages/AdminUsers.tsx`

---

## Change Log

- 2026-04-05 — Story S-E1-03: verified role assign/change end-to-end; tightened tests + role-only toast; sprint → review.
- 2026-04-06 — BMAD code review patch: case-insensitive role PATCH test; story + sprint → **done**.

---

## Definition of done (story-level)

- [x] All acceptance criteria (1–8) verified.
- [x] Admin-only enforcement confirmed for role changes.
- [x] No regression on S-E1-01 / S-E1-02 flows (login, user CRUD, deactivate).
