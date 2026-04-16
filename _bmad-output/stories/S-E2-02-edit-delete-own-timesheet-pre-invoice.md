---
workflowType: story-detail
status: done
storyId: S-E2-02
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

# S-E2-02 — Edit or delete own timesheet entries (pre-invoice)

## User story

**As an** employee, **I want** to edit or delete my own timesheet entries until invoiced, **so that** I can correct mistakes before billing lock.

**Traceability:** FR6

---

## Acceptance criteria

1. **Pre-invoice edit allowed:** A signed-in user can edit their own existing timesheet line fields (at minimum: `hours`, `isBillable`, `notes`) for weeks that are **not invoiced**.
2. **Pre-invoice delete allowed:** A signed-in user can delete their own existing timesheet line for weeks that are **not invoiced**.
3. **Invoiced lock enforced server-side:** If a timesheet line is invoiced, attempts to edit or delete it are rejected server-side with a consistent `{ message }` error body.
4. **Ownership enforced server-side:** A user cannot edit/delete another user’s timesheet lines (return `403 Forbidden`).
5. **UI supports edit/delete:** The `/timesheet` page supports removing a persisted line (delete) and editing a persisted line (edit + save), with clear success/failure feedback (toast).
6. **Tests:** Integration tests cover:
   - Editing updates persist and reload for same user
   - Deleting removes the line
   - Cross-user edit/delete forbidden
   - Invoiced line edit/delete is blocked

---

## Out of scope (explicit)

- Timesheet audit trail / tamper-evidence (S-E2-04 / FR8). Do not implement audit logging here.
- Manager/Finance org/team visibility queries (S-E2-05 / S-E2-06).
- Full invoice generation and traceability (Epic E10). This story only establishes the minimum “invoiced lock” primitive.

---

## Architecture alignment (must follow)

- **Repo reality (follow existing code, not aspirational architecture):**
  - Backend is `.NET` in `api/` (not `server/`)
  - SPA is in `web/` (not `client/`)
- **Routing conventions:** Keep existing `/api/...` routes. Do **not** introduce `/api/v1/`.
- **Auth/RBAC:** Use the existing JWT bearer pattern; enforce ownership server-side (never UI-only).
- **Error shape:** Match existing API error body used by `AuthController` and `TimesheetsController`: `{ "message": "..." }` (`AuthErrorResponse`).
- **Date/time:** Keep week semantics consistent with S-E2-01:
  - `weekStart` is `YYYY-MM-DD` and must be **Monday**
  - `workDate` is `YYYY-MM-DD`

---

## Data model changes (minimal, migration-friendly)

### `TimesheetLine` (existing)

Add a lock marker so the API can enforce “until invoiced” without implementing invoicing yet:

- Add `InvoicedAtUtc: DateTime?` (nullable)
  - `null` means **editable**
  - non-null means **locked**

This keeps the door open for Epic E10 to replace “mark invoiced” behavior with a real invoice relationship later.

---

## API changes

### 1) Include `id` + `invoicedAtUtc` in timesheet line responses

Update `TimesheetLineResponse` (and `web/src/api.ts` types) to include:

- `id: string` (Guid)
- `invoicedAtUtc: string | null` (ISO 8601 UTC)

Reason: the UI needs a stable identifier for delete, and the lock state needs to be visible for UX (disable delete button + show message).

### 2) Add delete endpoint (explicit delete; do not change week PUT semantics)

Add to `api/Controllers/TimesheetsController.cs`:

- `DELETE /api/timesheets/lines/{id:guid}`
  - Auth required
  - Ownership check: only owner can delete (else `403`)
  - If `InvoicedAtUtc != null` → reject with `409 Conflict` (or `400` if you’re keeping it simpler), body `{ message }`
  - On success: `204 No Content`

### 3) Add “mark invoiced” endpoint as a temporary hook

Because Epic E10 does not exist yet, we still need a way to set `InvoicedAtUtc` so the lock can be tested and demonstrated.

Add:

- `POST /api/timesheets/lines/{id:guid}/mark-invoiced`
  - Restricted: `Authorize(Roles = RbacRoleSets.AdminAndFinance)`
  - Sets `InvoicedAtUtc = DateTime.UtcNow` if not already set
  - Returns `204 No Content`

Guardrail: Keep the name and restriction explicit so it’s easy to delete/replace when real invoicing lands.

---

## Frontend changes (`web/`)

### Timesheet UI

Update `web/src/pages/TimesheetWeek.tsx`:

- When loading week lines, keep the `id` and `invoicedAtUtc` fields.
- **Delete behavior:**
  - If a line is persisted (has `id`):
    - If `invoicedAtUtc != null`, disable “Remove” (or show toast on click) with message like “This line is invoiced and cannot be deleted.”
    - Else call `DELETE /api/timesheets/lines/{id}` and refresh.
  - If a line is not persisted yet, keep current local “remove row” behavior.
- **Edit behavior:**
  - Editing via “Save” already works (week PUT upserts). Ensure the UI prevents editing locked lines:
    - Either disable inputs when `invoicedAtUtc != null`, or allow edits client-side but expect server to reject on save and show toast (prefer disabling).

### API client

Update `web/src/api.ts`:

- Extend `TimesheetLine` type to include `id` and `invoicedAtUtc`
- Add `deleteTimesheetLine(token, id)` helper

---

## Tests (`tests/C2E.Api.Tests`)

Add/extend integration tests (create a new test class if cleaner):

1. **Edit persists:** Create line via week PUT; then PUT again with same key but different `hours` and verify GET returns updated hours.
2. **Delete works:** Create line; GET to obtain `id`; DELETE; GET again shows it gone.
3. **Cross-user forbidden:** Create line as user1, obtain `id`, then attempt DELETE as user2 → `403`.
4. **Invoiced lock:** Create line; obtain `id`; mark invoiced via admin/finance endpoint; then:
   - attempt delete as owner → `409` (or chosen status) with `{ message }`
   - attempt edit via week PUT changing hours → reject with `{ message }` (status consistent with your choice)

Implementation note: “edit reject when invoiced” should be checked in the week upsert path too, otherwise users can still mutate locked lines via bulk save.

---

## Implementation notes / guardrails

- **Do not reintroduce implicit deletes** in `PUT /api/timesheets/week`. Keep week PUT as upsert-only. Deletes must be explicit via `DELETE /lines/{id}`.
- **Lock enforcement must be centralized:** enforce “invoiced lock” inside the server write paths:
  - In `UpsertWeekForUser`: if updating an existing row and it has `InvoicedAtUtc != null`, reject.
  - In `DELETE /lines/{id}`: same lock check.
- **Do not widen the auth surface:** “mark invoiced” is `AdminAndFinance` only.
- **Error shape is non-negotiable:** always return `{ message }` for user-facing failures.

---

## Definition of done (story-level)

- [x] User can edit their own timesheet lines (pre-invoice) and see changes persist after reload.
- [x] User can delete their own timesheet lines (pre-invoice).
- [x] Invoiced lines cannot be edited or deleted (server-enforced).
- [x] Cross-user edit/delete is forbidden (server-enforced).
- [x] Integration tests pass for edit/delete/lock/ownership.
- [x] `/timesheet` UI supports delete + communicates lock state clearly.

---

## References

- `_bmad-output/planning-artifacts/epics-and-stories.md` → Epic E2 → `S-E2-02` (FR6)
- `_bmad-output/planning-artifacts/prd.md` → FR6 “edit/delete own timesheet entries prior to invoice generation”
- `_bmad-output/planning-artifacts/architecture.md` → security: server-side RBAC; RFC 7807 is aspirational but this repo uses `{ message }` now
- `_bmad-output/planning-artifacts/ux-design-specification.md` → feedback patterns (toast), info-dense grid
- Existing code patterns: `api/Controllers/TimesheetsController.cs`, `web/src/pages/TimesheetWeek.tsx`, `tests/C2E.Api.Tests/TimesheetWeekTests.cs`

---

## Dev Agent Record

### Agent Model Used

GPT-5.2

### Completion Notes List

- Ultimate context engine analysis completed — comprehensive developer guide created.
- ✅ Added `InvoicedAtUtc` lock marker on `TimesheetLine` and enforced server-side lock on edit/delete (409 with `{ message }`).
- ✅ Added `DELETE /api/timesheets/lines/{id}` (owner-only).
- ✅ Added admin/finance-only `POST /api/timesheets/lines/{id}/mark-invoiced` (temporary hook).
- ✅ Updated `/timesheet` UI to delete persisted lines and disable edit/delete when locked.
- ✅ Added/extended integration tests for edit persistence, delete, cross-user forbidden, and invoiced lock.

### File List

- `_bmad-output/stories/S-E2-02-edit-delete-own-timesheet-pre-invoice.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `api/Controllers/TimesheetsController.cs`
- `api/Dtos/TimesheetWeekDtos.cs`
- `api/Models/TimesheetLine.cs`
- `tests/C2E.Api.Tests/TimesheetWeekTests.cs`
- `web/src/api.ts`
- `web/src/pages/TimesheetWeek.tsx`

### Change Log

- 2026-04-07: Implemented pre-invoice edit/delete with server-side invoiced lock + ownership enforcement; updated UI and integration tests.

## Tasks

### Review Findings

- [x] [Review][Decision] Locked line “rename” semantics (key-field edits) — **Decision**: hard-stop on locked edits (server-enforced). Implemented by rejecting any attempt to introduce new week keys when the week has any locked line (409 `{ message }`).

- [x] [Review][Patch] Return `{ message }` for 401/403 paths (avoid bare `Forbid()` / `Unauthorized()`) [`api/Controllers/TimesheetsController.cs`]
- [x] [Review][Patch] Prevent null-body NRE on week PUT (model binder can yield null list) [`api/Controllers/TimesheetsController.cs`]
- [x] [Review][Patch] Normalize `row.Notes` before lock comparison (`null` vs `""` mismatch causes false 409 on invoiced lines) [`api/Controllers/TimesheetsController.cs`]
- [x] [Review][Patch] Don’t bump `UpdatedAtUtc` for invoiced no-op “saves” (locked rows should not change timestamps) [`api/Controllers/TimesheetsController.cs`]
- [x] [Review][Patch] Enforce “week locked” cannot introduce new lines (hard-stop locked edits) [`api/Controllers/TimesheetsController.cs`]
- [x] [Review][Patch] UI “Remove” deletes persisted line even when user has unsaved edits — add confirm or dirty-state guard to avoid surprise destructive delete [`web/src/pages/TimesheetWeek.tsx`]
- [x] [Review][Patch] Use shared `authHeaders()` helper for delete API (consistency) [`web/src/api.ts`]

