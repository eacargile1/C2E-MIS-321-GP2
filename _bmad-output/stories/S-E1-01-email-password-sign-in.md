---
workflowType: story-detail
status: done
storyId: S-E1-01
epic: E1
derivedFrom:
  - '_bmad-output/planning-artifacts/epics-and-stories.md'
  - '_bmad-output/planning-artifacts/prd.md'
  - '_bmad-output/planning-artifacts/architecture.md'
completedAt: '2026-04-05'
inputDocuments:
  - '_bmad-output/planning-artifacts/epics-and-stories.md'
  - '_bmad-output/planning-artifacts/architecture.md'
---

# S-E1-01 — Sign in with email and password (app login)

## User story

**As an** employee, **I want** to sign in through a normal app login screen with my email and password, **so that** I can access the platform without company SSO or an external IdP.

**Traceability:** FR1

---

## Acceptance criteria

1. **Login UI:** Email and password fields with primary sign-in action; invalid credentials show a generic safe error (no account enumeration).
2. **Session:** Successful authentication establishes a session with a JWT (or equivalent) suitable for API authorization, per `architecture.md`.
3. **Credential storage:** Passwords persisted only as strong one-way hashes (e.g. ASP.NET Identity / PBKDF2-or-better); no plaintext passwords in the database; transport over TLS.

---

## Out of scope (explicit)

- Admin provisioning of users after initial login (S-E1-02).
- Role assignment UI (S-E1-03).
- Full RBAC matrix enforcement on all routes (S-E1-04) — this story delivers **authenticated identity**; policy middleware can be stubbed or minimal until S-E1-04.

---

## Architecture alignment

- **App login:** Email + password against the platform user store; no corporate OIDC required at launch.
- **JWT:** Align with `architecture.md` session model (30-min idle timeout in follow-up hardening; wire idle handling when refresh/session layer exists).
- **Backend:** C# API — validate credentials; issue signed JWT for API `Authorization: Bearer`; failed login → 401.
- **Frontend:** React SPA — login view; store tokens per app security policy (memory + httpOnly refresh pattern preferred once implemented).

---

## Implementation notes

| Area | Direction |
|------|-----------|
| Config | JWT signing key / issuer from env; no secrets in repo. |
| Accounts | Users exist before login (seeded or created by admin in S-E1-02); optional self-registration only if product agrees (default: admin-created accounts). |
| Hardening | Rate limit or lockout on repeated failures (MVP: basic throttle acceptable). |
| API | Protected routes expect `Authorization: Bearer <JWT>`; unauthenticated → 401. |

---

## Definition of done (story-level)

- [x] E2E or integration test: happy-path login with valid credentials (and failure path for bad password).
- [x] Documented setup for local dev (seed user or test account + env for JWT if needed).
- [x] No plaintext passwords stored in DB; login requests use HTTPS in non-local deployments.

---

## Tasks (dev-story)

- [x] Bootstrap .NET 9 API with JWT bearer + email/password validation
- [x] Hash passwords with `PasswordHasher<AppUser>` (PBKDF2); generic 401 message (no enumeration)
- [x] Seed dev user via config (`Seed:*` + `Jwt:SigningKey` in appsettings / env)
- [x] React SPA login UI; token used in memory only for session display (`/api/auth/me`)
- [x] Integration tests (`tests/C2E.Api.Tests`)

### Review Findings

- [x] [Review][Patch] JWT lifetime in token vs `ExpiresInSeconds` can disagree when `AccessTokenMinutes` ≤ 0 — clamp consistently in `JwtTokenService` (and validate at startup) [`api/Services/JwtTokenService.cs`, `api/Controllers/AuthController.cs`]
- [x] [Review][Patch] Empty `Cors:Origins` makes `WithOrigins` throw at startup — guard or default dev origins [`api/Program.cs`]
- [x] [Review][Patch] Default seed password and JWT signing placeholder live in committed `appsettings.json` — move secrets to Development / user secrets / env only [`api/appsettings.json`]
- [x] [Review][Patch] `PasswordVerificationResult.SuccessRehashNeeded` did not persist upgraded hash — update user row when rehash required [`api/Controllers/AuthController.cs`]
- [x] [Review][Patch] `login`/`me` assume `res.json()` always succeeds on OK responses — handle non-JSON / parse errors [`web/src/api.ts`]
- [x] [Review][Defer] No rate limiting on `POST /api/auth/login` (implementation notes; optional MVP throttle) [`api/Controllers/AuthController.cs`] — deferred, pre-existing gap vs hardening note
- [x] [Review][Defer] In-memory database only — not deployable persistence; tracked for CleanArch/DB follow-on [`api/Program.cs`] — deferred, pre-existing
- [x] [Review][Defer] JWT uses fixed expiry from issue time; architecture calls for idle timeout later — deferred until session/refresh layer [`api/Services/JwtTokenService.cs`] — deferred, pre-existing

---

## Dev agent record

**Completion notes:** MVP stack added: `api/` (C2E.Api), `web/` (Vite React TS), `tests/C2E.Api.Tests`. In-memory EF for local bootstrap; replace with real DB + CleanArch when following architecture starter stories. CORS allows `http://localhost:5173`. Override `Jwt:SigningKey` (≥32 chars) and `Seed:*` via env or appsettings in non-dev.

**Debug log:** `dotnet restore` initially failed in sandbox (CookieContainer); succeeded with full permissions.

---

## File list (this story)

| Area | Paths |
|------|--------|
| API | `api/Program.cs`, `api/C2E.Api.csproj`, `api/appsettings*.json`, `api/Controllers/AuthController.cs`, `api/Data/AppDbContext.cs`, `api/Models/AppUser.cs`, `api/Dtos/*`, `api/Options/JwtOptions.cs`, `api/Services/JwtTokenService.cs` |
| Web | `web/src/App.tsx`, `web/src/App.css`, `web/src/api.ts`, `web/.env.example`, `web/index.html`, `web/src/index.css` |
| Tests | `tests/C2E.Api.Tests/AuthLoginTests.cs`, `tests/C2E.Api.Tests/C2E.Api.Tests.csproj` |
| Solution | `C2E.sln` |

---

## Change log

| Date | Change |
|------|--------|
| 2026-04-05 | Initial implementation for S-E1-01 (API + web + integration tests). |

---

## Local dev (quick)

1. API: `cd api && dotnet run --launch-profile http` → listens on `http://localhost:5028` (see `Properties/launchSettings.json`).
2. Web: `cd web && npm run dev` → default `http://localhost:5173`; optional `web/.env` with `VITE_API_BASE_URL=http://localhost:5028`.
3. Sign in with seeded account: email `dev@c2e.local`, password `ChangeMe!1` (override via `Seed:DevUserEmail` / `Seed:DevUserPassword`). **Set a strong `Jwt:SigningKey` (env `Jwt__SigningKey`) for shared/non-local environments.**

---

## Dependencies

- **Blocks:** S-E1-02, S-E1-03, S-E1-04, and all secured domain stories.
- **Requires:** At least one way to have a user record before first login (seed script, migration seed, or minimal admin path — coordinate with S-E1-02 if admin creates users first).
