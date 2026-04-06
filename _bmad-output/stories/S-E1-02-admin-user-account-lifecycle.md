---
workflowType: story-detail
status: done
storyId: S-E1-02
epic: E1
derivedFrom:
  - '_bmad-output/planning-artifacts/epics-and-stories.md'
  - '_bmad-output/planning-artifacts/prd.md'
  - '_bmad-output/planning-artifacts/architecture.md'
  - '_bmad-output/planning-artifacts/ux-design-specification.md'
completedAt: '2026-04-05'
inputDocuments:
  - '_bmad-output/planning-artifacts/epics-and-stories.md'
  - '_bmad-output/planning-artifacts/architecture.md'
  - '_bmad-output/planning-artifacts/prd.md'
  - '_bmad-output/planning-artifacts/ux-design-specification.md'
---

# S-E1-02 — Admin: create, edit, and deactivate user accounts

## User story

**As an** admin, **I want** to create, edit, and deactivate user accounts, **so that** only active employees access the platform.

**Traceability:** FR2 · PRD § Authentication & User Management (“Admin provisions new employee accounts”)

---

## Acceptance criteria

1. **Create:** Admin can create a user with email + initial password (or system-generated temporary password with forced change — pick one approach and document it; MVP: admin sets initial password is acceptable). Email normalized and unique; password stored only as hash via existing `PasswordHasher<AppUser>` pattern.
2. **Edit:** Admin can update a user’s email (still unique, normalized) and reset password (re-hash; never persist plaintext).
3. **Deactivate:** Admin can deactivate an account (`IsActive = false` or equivalent). Deactivated users cannot sign in; use the **same generic login error** as bad credentials (no account enumeration). Reactivating an inactive user is in scope if product treats “deactivate” as reversible — **default: reversible** (toggle active) unless PRD explicitly says permanent; PRD says “deactivate,” implement soft deactivate + optional reactivate in same UI.
4. **Authorization:** Only **Admin** role (per PRD RBAC matrix: “Manage users & roles”) can call user-management APIs. Non-admin receives **403** with a clear, consistent JSON body (match existing API error shape used in `AuthController` / `web/src/api.ts` until Problem Details migration).
5. **Admin UI:** React SPA exposes an **Admin-only** users screen at **`/admin/users`** (UX default landing for Admin). List users (email, active status, role read-only for display). Create / edit / deactivate flows. Non-admin must not see admin nav or routes (client-side) and server must still enforce (server-side).
6. **Session claims:** JWT and **`GET /api/auth/me`** include enough information for the SPA to know **role** and **active** state for gating UI. Extend `MeResponse` and `JwtTokenService` accordingly.
7. **Tests:** Integration tests cover happy paths for admin create/edit/deactivate, forbidden access for non-admin, and **deactivated user cannot obtain a token** (login returns same shape as invalid password).

---

## Out of scope (explicit)

- **Role assignment UI and role-change API** — S-E1-03 (FR3). This story may persist a **default role** (e.g. `IC`) on create and show read-only role on the list for context; **do not** build the full “assign Admin/Manager/Finance/IC” workflow here.
- **Full RBAC on all domain routes** — S-E1-04. Only **admin user-management** endpoints need the Admin policy in this story.
- **Transactional outbox / Hangfire / SQL Server** — still deferred per S-E1-01 notes; remain on in-memory EF until the persistence story unless you explicitly pull DB forward.

---

## Architecture alignment

- **Server-side enforcement:** Admin-only user APIs are **never** UI-only; align with ADR-02 direction (this story implements a **narrow** policy slice).
- **Passwords:** Salted one-way hashes only; reuse `PasswordHasher<AppUser>` from S-E1-01.
- **API style:** REST, resource-oriented `/api/...` (architecture prefers `/api/v1/` long-term — current codebase uses `api/[controller]` without version; **stay consistent** with existing `AuthController` unless you introduce versioning in one pass across controllers).
- **Errors:** Architecture targets RFC 7807; if the codebase still uses bespoke DTOs, **new endpoints should match existing clients** or add Problem Details in a follow-up — do not split error shapes within one screen without updating `web/src/api.ts`.

---

## Data model (minimum)

| Addition | Purpose |
|----------|--------|
| `AppRole` enum | `Admin`, `Manager`, `Finance`, `IC` — matches PRD. |
| `AppUser.Role` | Required; seed dev user = `Admin`; new users default `IC`. |
| `AppUser.IsActive` | Default `true`; login rejects inactive with generic message. |

**Indexes:** Keep unique index on normalized email; consider composite queries by `IsActive` for admin list (no extra index required for MVP in-memory).

---

## API sketch (implement as proper controllers/DTOs)

| Method | Path | Auth | Behavior |
|--------|------|------|----------|
| `GET` | `/api/users` or `/api/admin/users` | Admin | List all users (id, email, role, isActive). |
| `POST` | same collection | Admin | Create user; body: email, password, optional flags. |
| `GET` | `.../{id}` | Admin | Single user. |
| `PATCH` or `PUT` | `.../{id}` | Admin | Update email and/or password; optional `isActive` toggle. |

Naming: prefer a dedicated `UsersController` or `AdminUsersController` — one place for these routes.

---

## Frontend sketch

- **Routing:** After login, if role is Admin, allow navigation to `/admin/users` (UX: Admin default route is `/admin/users`).
- **Patterns:** Toasts on success/failure (4s) per UX spec; **deactivate** uses **inline confirmation**, not a blocking modal stack (UX: “Irreversible actions… account deactivation… inline confirmation”).
- **Token:** Continue memory-only token holding from S-E1-01; attach `Authorization` on admin API calls.
- **Discoverability:** Admin nav entry only when `me.role === 'Admin'` (or equivalent).

---

## Implementation notes

| Area | Direction |
|------|-----------|
| Seed | Ensure seeded dev user is `Admin` + `IsActive` so implementers can call user APIs immediately. |
| Authorization | `AddAuthorization` + policy or roles: `[Authorize(Roles = "Admin")]` after role claims exist on the identity. Map `AppRole` enum to string claim values consistently. |
| Login | In `AuthController.Login`, after password verify, if `!user.IsActive` return same `Unauthorized` + `InvalidCredentialsMessage` as failed password. |
| Email | Normalize with `Trim().ToLowerInvariant()` on write and lookup (match login). |
| Self-lockout | Optional: prevent last Admin from deactivating themselves, or document as acceptable risk for MVP. |

---

## Definition of done (story-level)

- [x] Admin can complete create → list → edit → deactivate → confirm deactivated user cannot login (manual or automated).
- [x] Non-admin token receives 403 on user-management endpoints.
- [x] No plaintext passwords in DB or logs; integration tests green.
- [x] `MeResponse` exposes role (and any fields SPA needs) without breaking existing login flow.

---

## Tasks (dev-story)

- [x] Add `AppRole`, `AppUser.Role`, `AppUser.IsActive`; migrate/ensure-created in-memory schema
- [x] Seed: set dev user to Admin + active
- [x] `JwtTokenService`: add `ClaimTypes.Role`; map enum to string
- [x] `AuthController`: inactive user → generic unauthorized; `Me` returns role + isActive
- [x] New controller: CRUD users (Admin-only); validation (FluentValidation optional — not in repo yet)
- [x] Web: `/admin/users` page, API helpers, admin gating from `me`
- [x] Tests: `C2E.Api.Tests` — admin success paths, 403, deactivated login

### Review Findings

- [x] [Review][Patch] Deactivated accounts keep full API access until JWT expires — **fixed:** `RequireActiveUserMiddleware` after `UseAuthentication` rejects inactive/missing users with `AuthMessages.InvalidCredentials` JSON. [`api/Middleware/RequireActiveUserMiddleware.cs`, `api/Program.cs`]
- [x] [Review][Patch] AC6 JWT active claim — **fixed:** JWT includes `JwtCustomClaims.Active` (`"true"` / `"false"`). [`api/JwtCustomClaims.cs`, `api/Services/JwtTokenService.cs`]
- [x] [Review][Defer] `GET /api/auth/me` returns bare `401` with no `AuthErrorResponse` body when subject is missing, user not found, or inactive — minor inconsistency with login/403 JSON; clients already map to “Session invalid”. [`api/Controllers/AuthController.cs` ~64–70] — deferred, pre-existing shape

---

## Previous story intelligence (S-E1-01)

- **Stack:** `api/` .NET 9, `web/` Vite React TS, `tests/C2E.Api.Tests` WebApplicationFactory integration tests.
- **Auth:** `PasswordHasher<AppUser>`, JWT in `JwtTokenService`, `AuthController` login/me, `AppDbContext` + in-memory DB.
- **CORS:** Default `http://localhost:5173` when `Cors:Origins` empty.
- **Secrets:** JWT signing key and seed passwords via env / Development config — do not commit real secrets.
- **Client:** `web/src/api.ts` — handle non-JSON errors defensively (already addressed in S-E1-01 review).
- **Extend, don’t replace:** Build user management on the same patterns (DTOs folder, controllers, EF model).

---

## File structure (expected touchpoints)

| Area | Paths (extend as needed) |
|------|---------------------------|
| API | `api/Models/AppUser.cs`, `api/Data/AppDbContext.cs`, `api/Program.cs`, `api/Controllers/AuthController.cs`, `api/Services/JwtTokenService.cs`, `api/Dtos/*`, new `UsersController` (or equivalent) |
| Web | `web/src/App.tsx` or routed layout, `web/src/api.ts`, new `web/src/pages/AdminUsers.tsx` (or `features/admin/…`) |
| Tests | `tests/C2E.Api.Tests/*Users*.cs` |

---

## Dependencies

- **Requires:** S-E1-01 (login + JWT + user store).
- **Blocks:** S-E1-03 (role changes), S-E1-04 (full RBAC matrix), and any story that assumes admin-provisioned users beyond seed.

---

## Open points (resolve during implementation)

- **URL versioning:** If you add `/api/v1`, migrate `Auth` in the same PR to avoid two conventions.
- **Last admin:** Decide guard vs. documented footgun.

---

## Dev agent record

**Implementation plan:** Model `AppRole` + `AppUser.Role`/`IsActive`; JWT role claim + `RoleClaimType`; `GET /api/auth/me` loads user from DB (fresh `isActive`/`role`). `UsersController` at `/api/users` with `[Authorize(Roles = Admin)]`; JSON 403 via `IAuthorizationMiddlewareResultHandler` using `AuthErrorResponse`. Create defaults role `IC`; admin-set initial password (MVP). Deactivate = `isActive: false` with reactivate in UI; last active admin cannot be deactivated (409). Tests use unique in-memory DB name per factory to avoid parallel pollution.

**Completion notes:** All ACs covered: admin CRUD, 403 JSON for non-admin, inactive login same as bad password, SPA `/admin/users` with admin-only route + link, toasts ~4s, inline deactivate confirm. `dotnet test` (9) and `npm run build` / `npm run lint` pass.

**File list:**
- `api/Models/AppRole.cs` (new)
- `api/Models/AppUser.cs`
- `api/Data/AppDbContext.cs`
- `api/Program.cs`
- `api/Authorization/JsonForbiddenAuthorizationMiddlewareResultHandler.cs` (new)
- `api/Services/JwtTokenService.cs`
- `api/Dtos/MeResponse.cs`
- `api/Dtos/CreateUserRequest.cs` (new)
- `api/Dtos/UpdateUserRequest.cs` (new)
- `api/Dtos/UserResponse.cs` (new)
- `api/Controllers/AuthController.cs`
- `api/Controllers/UsersController.cs` (new)
- `tests/C2E.Api.Tests/AuthLoginTests.cs`
- `tests/C2E.Api.Tests/UsersAdminTests.cs` (new)
- `web/package.json`
- `web/src/api.ts`
- `web/src/App.tsx`
- `web/src/App.css`
- `web/src/pages/AdminUsers.tsx` (new)
- `_bmad-output/implementation-artifacts/sprint-status.yaml`

**Change log:** 2026-04-06 — Implemented S-E1-02 admin user lifecycle (API, JWT/me, SPA admin users, integration tests, isolated in-memory DB for tests).  
**Change log:** 2026-04-05 — Code review follow-up: `RequireActiveUserMiddleware`, JWT `active` claim, `AuthMessages` shared constant, +2 integration tests (11 total).

---

## Note on sprint tracking

No `_bmad-output/implementation-artifacts/sprint-status.yaml` was present. Run **sprint-planning** when you want automated “next backlog story” discovery; this file was created as the **sequential successor** to `S-E1-01`.
