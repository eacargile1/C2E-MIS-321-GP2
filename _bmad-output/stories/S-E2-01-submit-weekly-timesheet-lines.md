---
workflowType: story-detail
status: done
storyId: S-E2-01
epic: E2
derivedFrom:
  - '_bmad-output/planning-artifacts/epics-and-stories.md'
  - '_bmad-output/planning-artifacts/prd.md'
  - '_bmad-output/planning-artifacts/architecture.md'
  - '_bmad-output/planning-artifacts/ux-design-specification.md'
inputDocuments:
  - '_bmad-output/planning-artifacts/epics-and-stories.md'
  - '_bmad-output/planning-artifacts/prd.md'
  - '_bmad-output/planning-artifacts/architecture.md'
  - '_bmad-output/planning-artifacts/ux-design-specification.md'
createdAt: '2026-04-06'
---

# S-E2-01 — Submit weekly timesheet lines

## User story

**As an** employee, **I want** to submit weekly timesheet lines (project, client, task, date, hours, billable flag, notes), **so that** my work is captured for billing and reporting.

**Traceability:** FR5

---

## Acceptance criteria

1. **Timesheet week UI:** Signed-in users can open a Timesheet screen (route: **`/timesheet`**) showing a **single week** (Mon–Sun or Mon–Fri; pick one and document it) where they can add/edit timesheet lines before submission.
2. **Line fields:** Each timesheet line captures:
   - `workDate` (date)
   - `client` (string for now; future stories may normalize to `ClientId`)
   - `project` (string for now; future stories may normalize to `ProjectId`)
   - `task` (string)
   - `hours` (number; enforce \(0 < hours \le 24\); allow quarter-hour increments if feasible)
   - `isBillable` (boolean)
   - `notes` (optional string, max length enforced)
3. **Persist + reload:** Saving a week persists lines server-side and reloading the page shows the same lines for that user/week.
4. **Ownership:** A user can only create/read their **own** timesheet lines (server enforced). Non-owner access is denied.
5. **API errors:** Validation failures return a consistent JSON error shape (match existing patterns in `api/Controllers/AuthController.cs` + `web/src/api.ts`) so the SPA can surface readable messages.
6. **Basic UX consistency:** Save/submit actions provide clear feedback (toast or inline success), and loading states avoid full-page spinners (skeleton/table placeholder acceptable).
7. **Tests:** Integration tests cover creating lines for a week and fetching them back for the same user; also verify a different user cannot read/write another user’s lines.

---

## Out of scope (explicit)

- Editing/deleting rules and “until invoiced” lock (S-E2-02).
- Audit trail / tamper-evidence (S-E2-04).
- Manager/Finance org/team visibility queries (S-E2-05 / S-E2-06 / FR9–FR10) beyond keeping existing stubs compiling.
- Transactional outbox / Hangfire / SQL Server migrations from `architecture.md` (this repo currently uses in-memory EF; do not “half-migrate” storage in this story).

---

## Architecture alignment (must follow)

- **Backend stack:** `.NET 9` API in `api/` using EF Core InMemory currently (`api/Program.cs`, `api/Data/AppDbContext.cs`).
- **Auth/RBAC:** JWT bearer auth already wired; enforce ownership server-side and use role strings consistent with `AppRole` / `ClaimTypes.Role`. Do not rely on UI-only checks.
- **Controllers:** Keep controllers thin (existing code is simple controllers + EF). Do not introduce a second routing convention (current: `/api/...` not `/api/v1/...`).
- **Date/time:** Use ISO 8601 in API payloads; represent `workDate` as a date string (`YYYY-MM-DD`) or UTC midnight timestamp consistently. Pick one and use it everywhere in this story.

---

## Data model (minimum; keep it small and migration-friendly)

Add a new entity and `DbSet`:

- `TimesheetLine`
  - `Id: Guid`
  - `UserId: Guid` (FK to `AppUser.Id` logically; in-memory EF can still enforce relationships by convention)
  - `WorkDate: DateOnly` (or `DateTime` UTC date; keep consistent with API contract)
  - `Client: string` (max length)
  - `Project: string` (max length)
  - `Task: string` (max length)
  - `Hours: decimal`
  - `IsBillable: bool`
  - `Notes: string?` (max length)
  - `CreatedAtUtc`, `UpdatedAtUtc` (optional but recommended to support later audit needs)

Indexes:

- Unique-ish constraint to prevent duplicate accidental lines (choose one):
  - **Option A (simpler):** allow duplicates; client can show multiple rows intentionally.
  - **Option B (recommended):** unique index on `(UserId, WorkDate, Client, Project, Task)` and implement “upsert” semantics.

Pick and document the choice; ensure the UI behavior matches.

---

## API sketch (keep consistent with existing code style)

Add endpoints to `api/Controllers/TimesheetsController.cs` (and keep the existing `organization` stub intact, even if unused):

- `GET /api/timesheets/week?weekStart=YYYY-MM-DD`
  - Auth required
  - Returns the signed-in user’s lines for that week
- `PUT /api/timesheets/week?weekStart=YYYY-MM-DD`
  - Auth required
  - Body: array of lines (client/project/task/workDate/hours/isBillable/notes)
  - Server validates and persists (create/update/delete within-week if you support deletions; otherwise, only create/update)

DTOs: create under `api/Dtos/` (match existing conventions used by `AuthController` and `UsersController`).

Auth: derive `UserId` from JWT subject (`ClaimTypes.NameIdentifier`) exactly like `AuthController.Me`.

---

## Frontend sketch (minimal but real)

This repo’s SPA is still small (`web/src/App.tsx` routes only). Implement Timesheet as a new page and wire it in:

- Add `web/src/pages/TimesheetWeek.tsx` (or similar) and route it at **`/timesheet`**.
- Reuse the existing in-memory session token pattern from `web/src/App.tsx` (token passed to page props).
- Add `web/src/api.ts` helpers:
  - `getTimesheetWeek(token, weekStart)`
  - `putTimesheetWeek(token, weekStart, lines)`
- UX requirements to honor from `ux-design-specification.md`:
  - Information-dense weekly grid (Harvest mental model)
  - Keyboard-friendly data entry (basic tab order is fine for MVP)
  - Clear confirmation feedback on save

Do not introduce TanStack Query/Zustand/Tailwind/shadcn yet unless the repo already has them installed (they’re in the architecture target state, not necessarily in this codebase today).

---

## Implementation notes / guardrails

- **Don’t reinvent auth:** use existing login + `Authorization: Bearer` pattern.
- **Consistency:** keep JSON field casing camelCase (default `System.Text.Json`).
- **Validation:** enforce hours bounds, required strings, and max lengths server-side; mirror the most important checks client-side for UX.
- **Future-proofing:** store `client`/`project` as strings now, but keep DTOs and entity naming compatible with later introduction of `ClientId`/`ProjectId`.

---

## Definition of done (story-level)

- [ ] A signed-in user can create/save timesheet lines for a week and reload them successfully.
- [ ] A different signed-in user cannot read/write another user’s lines (403 or 404; be consistent).
- [ ] Integration tests pass (`tests/C2E.Api.Tests`) for the new endpoints and ownership checks.
- [ ] Minimal Timesheet screen is reachable at `/timesheet` in the SPA and uses the real API.

---

## Tasks (dev-story)

- [x] API: Add `TimesheetLine` model + `DbSet` + model configuration + any indexes (`api/Models/`, `api/Data/AppDbContext.cs`)
- [x] API: Implement week read/write endpoints in `api/Controllers/TimesheetsController.cs`
- [x] API: DTOs for request/response in `api/Dtos/`
- [x] Tests: Add integration tests for week read/write + ownership isolation
- [x] Web: Add `/timesheet` page + minimal editable week grid
- [x] Web: Add API functions in `web/src/api.ts` for week read/write
- [x] Web: Add nav/link from home for all roles (or role-aware redirect later; keep simple)

### Review Findings

- [x] [Review][Patch] Add explicit user-scoped endpoint(s) to enforce 403/404 on non-owner access (to satisfy AC4/DoD) and update tests accordingly.
- [x] [Review][Patch] Change PUT semantics to upsert-only (no implicit deletes when a line is omitted) and update UI/tests accordingly.

- [x] [Review][Patch] Enforce `weekStart` is Monday (not arbitrary 7-day window) [`api/Controllers/TimesheetsController.cs:28-33,66-71`]
- [x] [Review][Patch] Make validation failures consistently return `{ message }` (avoid `BadRequest(ModelState)` shape) [`api/Controllers/TimesheetsController.cs:64-65`]
- [x] [Review][Patch] Harden date parsing by trimming inputs (`weekStart`, `workDate`) before parsing [`api/Controllers/TimesheetsController.cs:28-29,66-67,81-84,169-170`]
- [x] [Review][Patch] Frontend should pre-validate hours: required, \(0 < hours \le 24\), quarter-hour increments; avoid `Number('') === 0` sending invalid 0 hours [`web/src/pages/TimesheetWeek.tsx:134-147`]
- [x] [Review][Patch] Frontend should guard against invalid `weekStart` state (avoid `NaN-NaN-NaN` / out-of-order loads) [`web/src/pages/TimesheetWeek.tsx:91-96`]

---

## References

- `_bmad-output/planning-artifacts/epics-and-stories.md` → Epic E2 → `S-E2-01` (FR5)
- `_bmad-output/planning-artifacts/prd.md` → FR5 “Timesheet & Time Tracking”
- `_bmad-output/planning-artifacts/architecture.md` → stack, auth patterns, routing conventions, error handling guidance
- `_bmad-output/planning-artifacts/ux-design-specification.md` → weekly grid UX + feedback patterns
- Existing code patterns: `api/Controllers/AuthController.cs`, `web/src/api.ts`, `web/src/App.tsx`

---

## Dev Agent Record

### Implementation Plan

- Week definition: **Monday–Sunday**, `weekStart` is Monday in `YYYY-MM-DD` format.
- API: `GET /api/timesheets/week?weekStart=YYYY-MM-DD` and `PUT /api/timesheets/week?weekStart=YYYY-MM-DD` with server-side ownership + validation.
- Storage: EF Core InMemory with `TimesheetLine` entity, keyed per-user and constrained to avoid accidental duplicates.
- Web: `/timesheet` route with minimal editable weekly table and Save feedback.

### Debug Log

- Added integration tests first; confirmed they failed with 404.
- Implemented API endpoints + model/DTOs; tests green.
- Added SPA page + API helpers; `npm run build` succeeded.

### Completion Notes

- ✅ Added weekly timesheet endpoints with ownership enforcement via JWT subject.
- ✅ Enforced validation (hours bounds + quarter-hour increments, required strings, workDate within week, consistent `{ message }` API errors).
- ✅ Added integration coverage for round-trip and cross-user isolation.

---

## File List

- `api/Controllers/TimesheetsController.cs`
- `api/Data/AppDbContext.cs`
- `api/Dtos/TimesheetWeekDtos.cs`
- `api/Models/TimesheetLine.cs`
- `tests/C2E.Api.Tests/TimesheetWeekTests.cs`
- `web/src/api.ts`
- `web/src/App.tsx`
- `web/src/pages/TimesheetWeek.tsx`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`

---

## Change Log

- 2026-04-06: Implemented weekly timesheet lines API + SPA page + integration tests.

---

## Status

done

