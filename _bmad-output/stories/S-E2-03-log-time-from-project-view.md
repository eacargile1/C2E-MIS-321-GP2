---
workflowType: story-detail
status: ready-for-dev
storyId: S-E2-03
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
createdAt: '2026-04-07'
---

# S-E2-03 — Log time from project view

## User story

**As an** employee, **I want** to log time from my project view, **so that** I don’t navigate away from context.

**Traceability:** FR7

---

## Acceptance criteria

1. **Project view exists:** Signed-in users can open a **Projects** area in the SPA that represents their “project view” context (minimum: a list of projects the user has worked on, and a detail page for a selected project).
2. **Log time in-context:** From a project detail page, a user can add a new time entry for that project by filling at minimum:
   - `workDate` (date, `YYYY-MM-DD`)
   - `task` (string)
   - `hours` (number, \(0 < hours \le 24\), 0.25 increments)
   - `isBillable` (boolean)
   - `notes` (optional)
   Client + Project come from the project context (not free-typed on the form by default).
3. **Persists and shows in timesheet:** After saving from the project view, the new line persists server-side and appears when the user opens `/timesheet` for the corresponding week.
4. **Ownership enforced server-side:** A user can only list their own projects and create time entries for themselves (derive user id from JWT subject server-side; do not accept `userId` input).
5. **Invoiced lock honored:** If the target week or target line would violate the existing invoiced lock rules introduced in `S-E2-02`, the API rejects with the existing consistent `{ message }` error body and the UI shows a clear failure toast.
6. **Tests:** Integration tests cover:
   - Creating a line from “project view” and then retrieving it via `GET /api/timesheets/week`
   - Cross-user isolation (user2 cannot see user1 project list, and user2 cannot mutate user1 data)

---

## Scope / design decisions (explicit)

- **Use repo reality, not aspirational architecture docs.** This repo currently uses:
  - SPA in `web/` with React Router and thin `web/src/api.ts`
  - API in `api/` with controller + EF Core InMemory patterns
  - Timesheet CRUD already exists in `api/Controllers/TimesheetsController.cs` and `web/src/pages/TimesheetWeek.tsx`
- **Do NOT build a full “Projects” domain module** (clients/projects tables, assignments, budgets) in this story. The “project view” for now is derived from existing timesheet data (`client` + `project` strings).
- **Do NOT change week PUT semantics** (keep `PUT /api/timesheets/week` upsert-only and explicit delete via `DELETE /api/timesheets/lines/{id}` as established in `S-E2-02`).

---

## Backend changes (API)

### New endpoints (minimal, timesheet-derived “project view”)

Add to `api/Controllers/TimesheetsController.cs`:

1) **List my projects (derived)**

- `GET /api/timesheets/projects`
  - Auth required
  - Returns distinct `(client, project)` pairs for the signed-in user
  - Suggested response DTO:
    - `client: string`
    - `project: string`
    - `lastWorkedOn: string` (optional; `YYYY-MM-DD`) or omit if you keep it simpler

Implementation guidance:
- Query `db.TimesheetLines` filtered by `UserId`, group by `(Client, Project)`.
- Prefer ordering by most recent `WorkDate` desc then client/project asc.

2) **Create a single line from project context**

- `POST /api/timesheets/lines`
  - Auth required
  - Body: `{ workDate, client, project, task, hours, isBillable, notes }`
  - Behavior:
    - Treat `(workDate, client, project, task)` as the line key (same as existing week upsert).
    - If a matching line exists:
      - If invoiced: reject `409` with `{ message }`
      - Else update hours/billable/notes (same rules as `UpsertWeekForUser`)
    - If no matching line exists:
      - If the week has any locked lines (existing “week locked” rule): reject `409` with `{ message }`
      - Else create new line.
  - Response:
    - Option A: `201 Created` with `TimesheetLineResponse` body (recommended, easiest for UI)
    - Option B: `204 No Content` (UI can just toast + optionally navigate)

DTOs:
- Add `TimesheetLineCreateRequest` (or `TimesheetLineCreateOrUpdateRequest`) under `api/Dtos/` mirroring validation rules in `TimesheetLineUpsertRequest`.

Error shape (non-negotiable):
- Match existing controller behavior: return `{ "message": "..." }` (via `AuthErrorResponse`) for user-facing failures.

---

## Frontend changes (SPA)

### Routes + pages

Add new pages under `web/src/pages/`:

1) `Projects.tsx`
- Lists the signed-in user’s projects from `GET /api/timesheets/projects`
- Each row links to a project detail route

2) `ProjectDetail.tsx`
- Route shape (pick one and stick to it):
  - Option A: `/projects/:client/:project` (URL-encoded segments)
  - Option B: `/projects/view?client=...&project=...`
- Shows project header (client + project)
- Contains a compact “Log time” form:
  - `workDate` defaults to today (or Monday of current week; choose one)
  - `task`, `hours`, `isBillable`, `notes`
  - client/project are shown read-only (or hidden) but sent in payload
- On save:
  - call new `createTimesheetLine(...)` API helper
  - show toast success/error (same toast pattern used in `TimesheetWeek.tsx`)
  - include a quick link/button: “Open this week in Timesheet” → navigates to `/timesheet` (no query param needed; timesheet already defaults to current week)

Update routing in `web/src/App.tsx`:
- Add routes for projects list + detail
- Update Home to include a link to Projects

### API client (`web/src/api.ts`)

Add:
- `type ProjectRef = { client: string; project: string; lastWorkedOn?: string | null }`
- `listTimesheetProjects(token): Promise<ProjectRef[]>`
- `createTimesheetLine(token, body): Promise<TimesheetLine>` (or void, depending on server response)

Keep:
- Error handling via existing `readApiErrorMessage`
- Auth header pattern via `authHeaders(token)`

---

## Testing requirements (integration tests)

Update or extend `tests/C2E.Api.Tests/`:

1) **Create from project view + appears in week**
- Arrange: sign in user1
- Act: `POST /api/timesheets/lines` with `{ workDate: MondayOfWeek, client, project, task, hours, isBillable, notes }`
- Assert:
  - Response is success and includes expected fields (if returning body)
  - `GET /api/timesheets/week?weekStart=...` includes that line

2) **Projects list is user-scoped**
- Arrange: user1 creates lines for (clientA, projectA); user2 creates lines for (clientB, projectB)
- Act:
  - user1 calls `GET /api/timesheets/projects` → includes only A
  - user2 calls same → includes only B

3) **Invoiced lock honored**
- Arrange: create a line, mark invoiced via existing admin/finance endpoint (`POST /api/timesheets/lines/{id}/mark-invoiced`)
- Act: attempt to create/update a line that would violate lock using `POST /api/timesheets/lines`
- Assert: `409` and `{ message }`

---

## Dev notes / guardrails (prevent common mistakes)

- **Don’t invent a ProjectsController.** This story is “project view” UX + timesheet-derived data only; keep API additions inside `TimesheetsController` to match current repo patterns.
- **Keep week semantics consistent:** `weekStart` is Monday and `workDate` uses `YYYY-MM-DD` everywhere (see existing `TimesheetsController.TryParseDateOnly`).
- **No UI-only security:** Do not accept `userId` in any request. Always derive from JWT claims.
- **Keep validation consistent:** reuse the same hours/date rules already enforced in `UpsertWeekForUser`.
- **Minimize new UI patterns:** reuse the existing simple toast stack approach from `web/src/pages/TimesheetWeek.tsx` (don’t introduce a new toast library here).

---

## Definition of done

- [ ] Projects list + project detail “project view” exist in the SPA and are reachable from Home.
- [ ] From project detail, user can create a timesheet line and see it later in `/timesheet` week view.
- [ ] API endpoints enforce user ownership and invoiced lock.
- [ ] Integration tests pass for create + project list scoping + invoiced lock.

---

## References

- `_bmad-output/planning-artifacts/epics-and-stories.md` → Epic E2 → `S-E2-03` (FR7)
- `_bmad-output/planning-artifacts/prd.md` → FR7 “log time directly from project view”
- `_bmad-output/planning-artifacts/ux-design-specification.md` → “Confirm, don’t interrupt” + info-dense patterns; keep the log-time flow in-context and keyboard-friendly where feasible
- Existing implementation patterns:
  - `api/Controllers/TimesheetsController.cs`
  - `api/Dtos/TimesheetWeekDtos.cs`
  - `api/Models/TimesheetLine.cs`
  - `web/src/pages/TimesheetWeek.tsx`
  - `web/src/api.ts`

---

## Dev Agent Record

### Agent Model Used

GPT-5.2

### Completion Notes List

- Ultimate context engine analysis completed — comprehensive developer guide created.

### File List

- `_bmad-output/stories/S-E2-03-log-time-from-project-view.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
