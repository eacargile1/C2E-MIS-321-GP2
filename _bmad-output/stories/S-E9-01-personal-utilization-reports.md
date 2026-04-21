# Story 9.1: Personal Utilization Reports

Status: done

## Story

As an employee (non-IC roles: Admin, Manager, Finance, Partner),
I want to see a personal report that breaks down my hours by client and project for a selected date range,
so that I can track my utilization and understand where my time is going beyond top-level totals.

## Acceptance Criteria

1. The existing `/reports` page KPI tiles (total hours, billable hours, non-billable hours, timesheet line count, expense totals) are preserved exactly as-is.
2. A new per-project breakdown table renders below the KPI tiles showing: Client | Project | Total Hours | Billable Hours | Non-Billable Hours — one row per unique client+project combination from timesheet lines in the selected period.
3. The breakdown table is sorted by total hours descending.
4. Rows sum to the same totals shown in the KPI tiles (no double-counting or mismatches).
5. If there are no timesheet lines in the period, the breakdown section shows "No timesheet entries in this period."
6. The breakdown data respects the same date range (from/to) as the existing period navigator — navigating months updates both KPI tiles and the breakdown table simultaneously.
7. A new API endpoint `GET /api/reports/personal-detail?from=YYYY-MM-DD&to=YYYY-MM-DD` returns the per-project breakdown for the authenticated user. Same RBAC as `personal-summary` (NonIc roles only).
8. The endpoint returns 400 if `from`/`to` are missing, unparseable, or `to < from` (same validation as `personal-summary`).
9. Frontend shows a loading state and an error toast if the detail endpoint fails (same pattern as existing summary loading).
10. A test covers the new endpoint: happy path returns correct grouped totals; non-IC call returns 403; invalid date returns 400.

## Tasks / Subtasks

- [x] **Backend: Add DTO** (AC: 7)
  - [x] Add `PersonalDetailResponse` and `PersonalDetailProjectRow` to `api/Dtos/ReportDtos.cs`
- [x] **Backend: Add endpoint** (AC: 7, 8)
  - [x] Add `GET personal-detail` action to `api/Controllers/ReportsController.cs`
  - [x] Reuse `TryGetUserId` and `TryParseDateOnly` helpers already on the controller
  - [x] Group `TimesheetLines` by `Client` + `Project`, sum `Hours` and `IsBillable` hours
  - [x] Return ordered by `TotalHours` descending
- [x] **Frontend: Wire API call** (AC: 6, 9)
  - [x] Add `PersonalDetail` type and `getPersonalDetail(token, from, to)` to `web/src/api.ts`
  - [x] Follow the same validation/parse pattern as `getPersonalSummary`
- [x] **Frontend: Render breakdown table** (AC: 1–6, 9)
  - [x] Add `detail` state alongside existing `summary` state in `Reports.tsx`
  - [x] Fetch both endpoints in parallel inside the existing `load` callback
  - [x] Render breakdown table below the KPI grid — use existing `admin-wrap` / `card` / `admin-card` CSS classes
  - [x] Table columns: Client, Project, Total Hours, Billable Hours, Non-Billable Hours
  - [x] Empty state: "No timesheet entries in this period."
  - [x] Loading state for breakdown section mirrors existing KPI loading hint
- [x] **Tests** (AC: 10)
  - [x] Add tests to `tests/C2E.Api.Tests/` — follow `TimesheetWeekTests.cs` / `ExpensesApiTests.cs` factory pattern

### Review Findings

- [x] [Review][Patch] Resolved decision: align both summary and detail endpoints to exclude soft-deleted lines [api/Controllers/ReportsController.cs:32]
- [x] [Review][Patch] KPI tiles are resilient when detail fetch fails [web/src/pages/Reports.tsx:50]
- [x] [Review][Defer] `personal-summary` may return 500 on missing query params due to null passed into `TryParseDateOnly` [api/Controllers/ReportsController.cs:24] — deferred, pre-existing

## Dev Notes

### Critical: Actual Stack vs Architecture Doc

**Ignore the `architecture.md` descriptions of TanStack Query, TanStack Router, Zustand, Tailwind, Clean Architecture, MediatR.** Those are aspirational. The real codebase is:

| Concern | Reality |
|---|---|
| Backend | Flat ASP.NET Core controllers in `api/Controllers/` — NO MediatR, NO Clean Architecture layers |
| ORM | EF Core with Npgsql (Postgres on Heroku) or InMemory (tests) |
| Frontend | Plain React 19 + `useState`/`useEffect`/`useCallback`/`useMemo` — NO TanStack Query, NO Zustand |
| API calls | Custom `fetch` wrappers in `web/src/api.ts` — follow existing patterns exactly |
| Styling | `App.css` + `index.css` CSS classes — NO Tailwind |
| Routing | `react-router-dom` v7 `BrowserRouter` — NOT TanStack Router |

### Existing Code to Reuse (Do Not Reinvent)

**Backend — all in `api/Controllers/ReportsController.cs`:**
- `TryGetUserId(out Guid id)` — reads user ID from JWT claims. Use this, don't re-implement.
- `TryParseDateOnly(string input, out DateOnly date)` — parses `yyyy-MM-dd`. Use this.
- Existing `personal-summary` endpoint is the model — copy its structure, just change the query and DTO.
- `[Authorize(Roles = RbacRoleSets.NonIc)]` — same RBAC for the new endpoint.

**Frontend — all in `web/src/pages/Reports.tsx`:**
- Month navigator, anchor/from/to/label state, `pushToast`, `load` callback, toast stack — all reuse as-is.
- Add `detail` state (`PersonalDetail | null`) initialized to `null`.
- Fetch `getPersonalDetail` inside the existing `load` callback alongside `getPersonalSummary` (use `Promise.all` or sequential — sequential is fine for simplicity, matching existing pattern).
- On error: `setDetail(null)` + `pushToast(...)`.

**CSS classes to use (from `App.css`/`index.css`):**
- Outer: `admin-wrap`, `card admin-card`
- Table wrapper: existing inline table pattern (see `AdminUsers.tsx` which has `<table>` with `admin-table` class or similar)
- Headings: `admin-h2`
- Empty/hint text: `admin-hint`
- Loading text: `admin-hint`

### Backend: New DTO Shape

Add to `api/Dtos/ReportDtos.cs`:

```csharp
public sealed class PersonalDetailProjectRow
{
    public required string Client { get; init; }
    public required string Project { get; init; }
    public required decimal TotalHours { get; init; }
    public required decimal BillableHours { get; init; }
    public required decimal NonBillableHours { get; init; }
}

public sealed class PersonalDetailResponse
{
    public required string From { get; init; }
    public required string To { get; init; }
    public required IReadOnlyList<PersonalDetailProjectRow> Rows { get; init; }
}
```

### Backend: New Endpoint Logic

Add to `ReportsController` (after the existing `PersonalSummary` action):

```csharp
[HttpGet("personal-detail")]
[Authorize(Roles = RbacRoleSets.NonIc)]
public async Task<ActionResult<PersonalDetailResponse>> PersonalDetail(
    [FromQuery] string from,
    [FromQuery] string to,
    CancellationToken ct)
{
    if (!TryGetUserId(out var userId)) return Unauthorized();
    if (!TryParseDateOnly(from, out var fromDate) || !TryParseDateOnly(to, out var toDate))
        return BadRequest(new AuthErrorResponse { Message = "Invalid from/to. Use YYYY-MM-DD." });
    if (toDate < fromDate)
        return BadRequest(new AuthErrorResponse { Message = "to must be on or after from." });

    var lines = await db.TimesheetLines
        .AsNoTracking()
        .Where(l => l.UserId == userId
                 && l.WorkDate >= fromDate
                 && l.WorkDate <= toDate
                 && !l.IsDeleted)
        .ToListAsync(ct);

    var rows = lines
        .GroupBy(l => (l.Client, l.Project))
        .Select(g => new PersonalDetailProjectRow
        {
            Client = g.Key.Client,
            Project = g.Key.Project,
            TotalHours = g.Sum(l => l.Hours),
            BillableHours = g.Where(l => l.IsBillable).Sum(l => l.Hours),
            NonBillableHours = g.Where(l => !l.IsBillable).Sum(l => l.Hours),
        })
        .OrderByDescending(r => r.TotalHours)
        .ToList();

    return Ok(new PersonalDetailResponse
    {
        From = fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        To = toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Rows = rows,
    });
}
```

Note: `!l.IsDeleted` filter — `TimesheetLine` has `IsDeleted` / `DeletedAtUtc` fields. Always exclude deleted lines.

### Frontend: New Type + API Function

Add to `web/src/api.ts` (after the existing `PersonalSummary` type and `getPersonalSummary`):

```typescript
export type PersonalDetailRow = {
  client: string
  project: string
  totalHours: number
  billableHours: number
  nonBillableHours: number
}

export type PersonalDetail = {
  from: string
  to: string
  rows: PersonalDetailRow[]
}

export async function getPersonalDetail(token: string, from: string, to: string): Promise<PersonalDetail> {
  const qs = new URLSearchParams({ from, to })
  const res = await fetch(`${base}/api/reports/personal-detail?${qs}`, {
    headers: { Authorization: `Bearer ${token}` },
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load detail report'))
  const r = (await res.json()) as Record<string, unknown>
  if (typeof r.from !== 'string' || typeof r.to !== 'string' || !Array.isArray(r.rows))
    throw new Error('Could not load detail report')
  const rows = r.rows.map((row) => {
    const x = row as Record<string, unknown>
    if (
      typeof x.client !== 'string' ||
      typeof x.project !== 'string' ||
      typeof x.totalHours !== 'number' ||
      typeof x.billableHours !== 'number' ||
      typeof x.nonBillableHours !== 'number'
    )
      throw new Error('Could not load detail report')
    return {
      client: x.client,
      project: x.project,
      totalHours: x.totalHours,
      billableHours: x.billableHours,
      nonBillableHours: x.nonBillableHours,
    }
  })
  return { from: r.from, to: r.to, rows }
}
```

### Frontend: Reports.tsx Changes

1. Add import for new function at top: `import { getPersonalSummary, getPersonalDetail, ... } from '../api'`
2. Add state: `const [detail, setDetail] = useState<PersonalDetail | null>(null)`
3. In `load` callback, fetch both in sequence (or `Promise.all`) — simplest: add after `getPersonalSummary`:
   ```typescript
   const d = await getPersonalDetail(token, from, to)
   setDetail(d)
   ```
   Wrap in same try/catch (if either fails, `setDetail(null)` and push toast).
4. Add breakdown table card after the existing KPI card — before the toast stack:

```tsx
<div className="card admin-card">
  <h2 className="admin-h2">Hours by Client & Project</h2>
  {loading ? (
    <p className="admin-hint">Loading…</p>
  ) : detail && detail.rows.length > 0 ? (
    <table style={{ width: '100%', borderCollapse: 'collapse' }}>
      <thead>
        <tr>
          <th style={{ textAlign: 'left', padding: '6px 8px' }}>Client</th>
          <th style={{ textAlign: 'left', padding: '6px 8px' }}>Project</th>
          <th style={{ textAlign: 'right', padding: '6px 8px' }}>Total h</th>
          <th style={{ textAlign: 'right', padding: '6px 8px' }}>Billable h</th>
          <th style={{ textAlign: 'right', padding: '6px 8px' }}>Non-Billable h</th>
        </tr>
      </thead>
      <tbody>
        {detail.rows.map((row) => (
          <tr key={`${row.client}||${row.project}`}>
            <td style={{ padding: '6px 8px' }}>{row.client}</td>
            <td style={{ padding: '6px 8px' }}>{row.project}</td>
            <td style={{ textAlign: 'right', padding: '6px 8px' }}>{row.totalHours.toFixed(2)}</td>
            <td style={{ textAlign: 'right', padding: '6px 8px' }}>{row.billableHours.toFixed(2)}</td>
            <td style={{ textAlign: 'right', padding: '6px 8px' }}>{row.nonBillableHours.toFixed(2)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  ) : (
    <p className="admin-hint">No timesheet entries in this period.</p>
  )}
</div>
```

### Tests: Pattern to Follow

File: `tests/C2E.Api.Tests/ReportsApiTests.cs` (create new file).

Use the same `WebApplicationFactory<Program>` factory pattern as `TimesheetWeekTests.cs`:
- Unique `Database:InMemoryName` (GUID suffix) per test class
- Valid `Jwt:SigningKey` ≥ 32 chars in `UseSetting`
- Seed a user via `DevRoleAccountsSeed` or direct DB insert
- Mint a JWT for that user using `IJwtTokenService` (resolved from factory services)

Required test cases:
1. **Happy path** — seed a Manager user, insert 2 timesheet lines (different client/project combos), call `GET /api/reports/personal-detail?from=...&to=...`, assert `200` and rows match expected groupings and hours.
2. **IC user** — seed an IC user, call endpoint, assert `403`.
3. **Missing dates** — call without `from` or `to`, assert `400`.
4. **Invalid date** — call with `from=not-a-date`, assert `400`.
5. **to < from** — call with inverted range, assert `400`.

### Project Structure — Files to Touch

| File | Action |
|---|---|
| `api/Dtos/ReportDtos.cs` | Add `PersonalDetailProjectRow` + `PersonalDetailResponse` |
| `api/Controllers/ReportsController.cs` | Add `PersonalDetail` action |
| `web/src/api.ts` | Add `PersonalDetailRow`, `PersonalDetail` types + `getPersonalDetail` function |
| `web/src/pages/Reports.tsx` | Add `detail` state, fetch in `load`, render breakdown table |
| `tests/C2E.Api.Tests/ReportsApiTests.cs` | New test file |

**Do not touch any other files.** No migrations needed — this is read-only data.

### RBAC Note

The nav and route guard currently hide `/reports` from IC users (`isIcOnly` check in `App.tsx`). This is intentional per current implementation. FR36 says "employees" but the team has chosen to restrict personal reports to non-IC roles consistent with other report-adjacent views. Do not change RBAC or nav guards in this story.

### Regression Risk

- Existing `personal-summary` endpoint and its frontend usage must not change.
- `Reports.tsx` currently has one load callback that fetches only `getPersonalSummary`. Adding `getPersonalDetail` to the same callback keeps loading state unified — both show "Loading…" and both reset on error. This is intentional.

## Dev Agent Record

### Agent Model Used

composer

### Debug Log References

### Completion Notes List

- Implemented `GET /api/reports/personal-detail` with same timesheet line filter as `personal-summary` so KPI totals and breakdown row sums stay consistent (AC4).
- `from`/`to` query binding uses null-coalescing before parse so missing params return 400.
- Frontend: `Promise.all` for summary + detail; second card uses `admin-table`; empty vs error distinguished (`detail` null → "No data." after failed load).
- Code review fixes applied: both report endpoints now exclude soft-deleted timesheet lines; report load now fetches summary first so KPI tiles remain visible even if detail fetch fails (error still surfaces via toast).

### File List

- `api/Dtos/ReportDtos.cs`
- `api/Controllers/ReportsController.cs`
- `web/src/api.ts`
- `web/src/pages/Reports.tsx`
- `tests/C2E.Api.Tests/ReportsApiTests.cs`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/stories/S-E9-01-personal-utilization-reports.md`

### Change Log

- 2026-04-21: Story S-E9-01 — personal utilization detail API, Reports UI breakdown, `ReportsApiTests`.
- 2026-04-21: Code review follow-up — fixed deleted-line filter parity and KPI resilience on detail fetch failure.
