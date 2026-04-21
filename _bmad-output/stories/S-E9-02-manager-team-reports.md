# Story 9.2: Manager Team Reports

Status: done

## Story

As a Manager or Admin,
I want to see a team report showing hours and expense totals per employee across my direct reports for a selected date range,
so that I can monitor team utilization and delivery contributions at a glance.

## Acceptance Criteria

1. A new section "Team Report" appears on the `/reports` page **only** for users with role `Manager` or `Admin`. Finance and Partner do not see this section.
2. The team section uses the same month navigator (from/to) already on the page — no separate date control needed.
3. For a **Manager**: the table shows one row per active direct report (users where `ManagerUserId == currentUserId`). If the manager has no direct reports, show "No direct reports found."
4. For an **Admin**: the table shows one row per active user in the org, sorted by displayName.
5. Each row shows: Employee name, Role, Total Hours, Billable Hours, Non-Billable Hours, Timesheet Line Count, Expense Count, Pending Expense $, Approved Expense $.
6. Rows are sorted by total hours descending (so highest contributors appear first).
7. A new API endpoint `GET /api/reports/team-summary?from=YYYY-MM-DD&to=YYYY-MM-DD` returns the per-member data. RBAC: `AdminAndManager` only (403 for other roles).
8. The endpoint returns 400 for missing/unparseable dates or `to < from`, same as `personal-summary`.
9. An IC, Finance, or Partner calling the endpoint directly gets 403.
10. Frontend shows a loading hint and error toast for the team section if the endpoint fails, consistent with the rest of the page.
11. Tests cover: Manager sees only their direct reports, Admin sees all, IC/Finance get 403, invalid dates get 400.

## Tasks / Subtasks

- [x] **Backend: Add DTOs** (AC: 7)
  - [x] Add `TeamMemberSummaryRow` and `TeamSummaryResponse` to `api/Dtos/ReportDtos.cs`
- [x] **Backend: Add endpoint** (AC: 3–9)
  - [x] Add `GET team-summary` action to `api/Controllers/ReportsController.cs`
  - [x] Admin path: fetch all active users; Manager path: fetch users where `ManagerUserId == userId`
  - [x] Bulk-load matching `TimesheetLines` and `ExpenseEntries` for the date range in two queries
  - [x] Group in-memory by UserId, compute aggregates, return ordered by TotalHours desc
- [x] **Frontend: Wire API call** (AC: 2, 10)
  - [x] Add `TeamMemberRow`, `TeamSummary` types and `getTeamSummary(token, from, to)` to `web/src/api.ts`
- [x] **Frontend: Render team section** (AC: 1–6, 10)
  - [x] Add `teamSummary` state and conditional fetch in `Reports.tsx` (only when role is Manager or Admin)
  - [x] Render team table card after the personal breakdown card
  - [x] Empty state: "No direct reports found."
  - [x] Loading/error mirrors existing personal section pattern
- [x] **Tests** (AC: 11)
  - [x] Add cases to `tests/C2E.Api.Tests/ReportsApiTests.cs`

### Review Findings

- [x] [Review][Patch] Add Partner 403 coverage for `team-summary` (AC9/AC11) [tests/C2E.Api.Tests/ReportsApiTests.cs]
- [x] [Review][Patch] Add missing `from`/`to` 400 tests for `team-summary` (AC8/AC11) [tests/C2E.Api.Tests/ReportsApiTests.cs]
- [x] [Review][Defer] `personal-summary` can throw on missing query params because null is passed into `TryParseDateOnly` [api/Controllers/ReportsController.cs:21] — deferred, pre-existing

## Dev Notes

### Critical: Actual Stack (Same Warning as S-E9-01)

**Ignore architecture.md references** to TanStack Query, Zustand, Tailwind, MediatR, Clean Architecture. The real stack:

| Concern | Reality |
|---|---|
| Backend | Flat controllers in `api/Controllers/` |
| Frontend | Plain React `useState`/`useEffect`/`useCallback` — no TanStack Query |
| API calls | `fetch` wrappers in `web/src/api.ts` |
| Styling | `App.css` / `index.css` CSS classes |
| Routing | `react-router-dom` v7 |

### The Canonical Direct-Reports Filter Pattern

**This is already in the codebase** — `ExpensesController.ListTeam` (lines 60–76) is the gold standard. Copy this pattern exactly:

```csharp
// Admin sees everything; Manager sees only their direct reports
var q = db.SomeTable.AsNoTracking().AsQueryable();
if (!User.IsInRole(nameof(AppRole.Admin)))
    q = q.Where(x => db.Users.Any(u => u.Id == x.UserId && u.ManagerUserId == userId));
```

For the team-summary endpoint the pattern is slightly different because we fetch `AppUser` rows first (not expenses/timesheets directly), so:

```csharp
List<AppUser> members;
if (User.IsInRole(nameof(AppRole.Admin)))
    members = await db.Users.AsNoTracking()
        .Where(u => u.IsActive)
        .OrderBy(u => u.DisplayName).ThenBy(u => u.Email)
        .ToListAsync(ct);
else
    members = await db.Users.AsNoTracking()
        .Where(u => u.ManagerUserId == userId && u.IsActive)
        .OrderBy(u => u.DisplayName).ThenBy(u => u.Email)
        .ToListAsync(ct);
```

### Backend: New DTOs

Add to `api/Dtos/ReportDtos.cs`:

```csharp
public sealed class TeamMemberSummaryRow
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public required string Role { get; init; }
    public required decimal TotalHours { get; init; }
    public required decimal BillableHours { get; init; }
    public required decimal NonBillableHours { get; init; }
    public required int TimesheetLineCount { get; init; }
    public required int ExpenseCount { get; init; }
    public required decimal ExpensePendingTotal { get; init; }
    public required decimal ExpenseApprovedTotal { get; init; }
}

public sealed class TeamSummaryResponse
{
    public required string From { get; init; }
    public required string To { get; init; }
    public required IReadOnlyList<TeamMemberSummaryRow> Rows { get; init; }
}
```

### Backend: Full Endpoint Implementation

Add after `PersonalDetail` in `ReportsController`:

```csharp
/// <summary>Team hours + expense rollup per direct report (Manager) or all users (Admin).</summary>
[HttpGet("team-summary")]
[Authorize(Roles = RbacRoleSets.AdminAndManager)]
public async Task<ActionResult<TeamSummaryResponse>> TeamSummary(
    [FromQuery] string from,
    [FromQuery] string to,
    CancellationToken ct)
{
    if (!TryGetUserId(out var userId)) return Unauthorized();
    if (!TryParseDateOnly(from, out var fromDate) || !TryParseDateOnly(to, out var toDate))
        return BadRequest(new AuthErrorResponse { Message = "Invalid from/to. Use YYYY-MM-DD." });
    if (toDate < fromDate)
        return BadRequest(new AuthErrorResponse { Message = "to must be on or after from." });

    // Determine member scope
    List<AppUser> members;
    if (User.IsInRole(nameof(AppRole.Admin)))
        members = await db.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.DisplayName).ThenBy(u => u.Email)
            .ToListAsync(ct);
    else
        members = await db.Users.AsNoTracking()
            .Where(u => u.ManagerUserId == userId && u.IsActive)
            .OrderBy(u => u.DisplayName).ThenBy(u => u.Email)
            .ToListAsync(ct);

    var memberIds = members.Select(m => m.Id).ToList();

    // Two bulk queries — do not query per-user in a loop
    var lines = await db.TimesheetLines
        .AsNoTracking()
        .Where(l => memberIds.Contains(l.UserId)
                 && l.WorkDate >= fromDate
                 && l.WorkDate <= toDate
                 && !l.IsDeleted)
        .ToListAsync(ct);

    var expenses = await db.ExpenseEntries
        .AsNoTracking()
        .Where(e => memberIds.Contains(e.UserId)
                 && e.ExpenseDate >= fromDate
                 && e.ExpenseDate <= toDate)
        .ToListAsync(ct);

    var linesByUser = lines.GroupBy(l => l.UserId)
        .ToDictionary(g => g.Key, g => g.ToList());
    var expensesByUser = expenses.GroupBy(e => e.UserId)
        .ToDictionary(g => g.Key, g => g.ToList());

    static decimal SumByStatus(List<ExpenseEntry> xs, ExpenseStatus s) =>
        xs.Where(x => x.Status == s).Sum(x => x.Amount);

    var rows = members
        .Select(m =>
        {
            var ul = linesByUser.GetValueOrDefault(m.Id, []);
            var ue = expensesByUser.GetValueOrDefault(m.Id, []);
            var total = ul.Sum(l => l.Hours);
            var billable = ul.Where(l => l.IsBillable).Sum(l => l.Hours);
            return new TeamMemberSummaryRow
            {
                UserId = m.Id,
                Email = m.Email,
                DisplayName = string.IsNullOrWhiteSpace(m.DisplayName)
                    ? m.Email
                    : m.DisplayName,
                Role = m.Role.ToString(),
                TotalHours = total,
                BillableHours = billable,
                NonBillableHours = total - billable,
                TimesheetLineCount = ul.Count,
                ExpenseCount = ue.Count,
                ExpensePendingTotal = SumByStatus(ue, ExpenseStatus.Pending),
                ExpenseApprovedTotal = SumByStatus(ue, ExpenseStatus.Approved),
            };
        })
        .OrderByDescending(r => r.TotalHours)
        .ToList();

    return Ok(new TeamSummaryResponse
    {
        From = fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        To = toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Rows = rows,
    });
}
```

**Anti-pattern avoided:** Do NOT query TimesheetLines per user in a loop (`N+1`). Load all matching lines for `memberIds` in one query, then group in memory.

### Frontend: New Types and API Function

Add to `web/src/api.ts` (after `getPersonalDetail`):

```typescript
export type TeamMemberRow = {
  userId: string
  email: string
  displayName: string
  role: string
  totalHours: number
  billableHours: number
  nonBillableHours: number
  timesheetLineCount: number
  expenseCount: number
  expensePendingTotal: number
  expenseApprovedTotal: number
}

export type TeamSummary = {
  from: string
  to: string
  rows: TeamMemberRow[]
}

export async function getTeamSummary(token: string, from: string, to: string): Promise<TeamSummary> {
  const qs = new URLSearchParams({ from, to })
  const res = await fetch(`${base}/api/reports/team-summary?${qs}`, {
    headers: { Authorization: `Bearer ${token}` },
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load team report'))
  const r = (await res.json()) as Record<string, unknown>
  if (typeof r.from !== 'string' || typeof r.to !== 'string' || !Array.isArray(r.rows))
    throw new Error('Could not load team report')
  const rows = r.rows.map((row) => {
    const x = row as Record<string, unknown>
    if (
      typeof x.userId !== 'string' ||
      typeof x.email !== 'string' ||
      typeof x.displayName !== 'string' ||
      typeof x.role !== 'string' ||
      typeof x.totalHours !== 'number' ||
      typeof x.billableHours !== 'number' ||
      typeof x.nonBillableHours !== 'number' ||
      typeof x.timesheetLineCount !== 'number' ||
      typeof x.expenseCount !== 'number' ||
      typeof x.expensePendingTotal !== 'number' ||
      typeof x.expenseApprovedTotal !== 'number'
    )
      throw new Error('Could not load team report')
    return {
      userId: x.userId,
      email: x.email,
      displayName: x.displayName,
      role: x.role,
      totalHours: x.totalHours,
      billableHours: x.billableHours,
      nonBillableHours: x.nonBillableHours,
      timesheetLineCount: x.timesheetLineCount,
      expenseCount: x.expenseCount,
      expensePendingTotal: x.expensePendingTotal,
      expenseApprovedTotal: x.expenseApprovedTotal,
    }
  })
  return { from: r.from, to: r.to, rows }
}
```

### Frontend: Reports.tsx Changes

**Imports:** Add `getTeamSummary`, `type TeamSummary` to the import from `'../api'`.

**Role guard constant** (add near top of component):
```typescript
const isManagerOrAdmin = profile.role === 'Manager' || profile.role === 'Admin'
```

**New state** (add alongside `summary` and `detail`):
```typescript
const [teamSummary, setTeamSummary] = useState<TeamSummary | null>(null)
```

**In the `load` callback** — add conditional team fetch after the personal calls:
```typescript
if (isManagerOrAdmin) {
  const t = await getTeamSummary(token, from, to)
  setTeamSummary(t)
} else {
  setTeamSummary(null)
}
```
Place inside the same try/catch. On catch: `setTeamSummary(null)`.

**Render** (add after the personal breakdown card, before the toast stack):
```tsx
{isManagerOrAdmin && (
  <div className="card admin-card">
    <h2 className="admin-h2">
      {profile.role === 'Admin' ? 'All Employees' : 'Direct Reports'} — Team Report
    </h2>
    {loading ? (
      <p className="admin-hint">Loading…</p>
    ) : teamSummary && teamSummary.rows.length > 0 ? (
      <table style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead>
          <tr>
            <th style={{ textAlign: 'left', padding: '6px 8px' }}>Employee</th>
            <th style={{ textAlign: 'left', padding: '6px 8px' }}>Role</th>
            <th style={{ textAlign: 'right', padding: '6px 8px' }}>Total h</th>
            <th style={{ textAlign: 'right', padding: '6px 8px' }}>Billable h</th>
            <th style={{ textAlign: 'right', padding: '6px 8px' }}>Non-Bill h</th>
            <th style={{ textAlign: 'right', padding: '6px 8px' }}>Expenses</th>
            <th style={{ textAlign: 'right', padding: '6px 8px' }}>Pending $</th>
            <th style={{ textAlign: 'right', padding: '6px 8px' }}>Approved $</th>
          </tr>
        </thead>
        <tbody>
          {teamSummary.rows.map((row) => (
            <tr key={row.userId}>
              <td style={{ padding: '6px 8px' }}>{row.displayName}</td>
              <td style={{ padding: '6px 8px' }}>{row.role}</td>
              <td style={{ textAlign: 'right', padding: '6px 8px' }}>{row.totalHours.toFixed(2)}</td>
              <td style={{ textAlign: 'right', padding: '6px 8px' }}>{row.billableHours.toFixed(2)}</td>
              <td style={{ textAlign: 'right', padding: '6px 8px' }}>{row.nonBillableHours.toFixed(2)}</td>
              <td style={{ textAlign: 'right', padding: '6px 8px' }}>{row.expenseCount}</td>
              <td style={{ textAlign: 'right', padding: '6px 8px' }}>{row.expensePendingTotal.toFixed(2)}</td>
              <td style={{ textAlign: 'right', padding: '6px 8px' }}>{row.expenseApprovedTotal.toFixed(2)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    ) : (
      <p className="admin-hint">
        {profile.role === 'Admin' ? 'No active users found.' : 'No direct reports found.'}
      </p>
    )}
  </div>
)}
```

### Tests: Cases to Add to `ReportsApiTests.cs`

The file should already exist from S-E9-01. Add a new test class or region for `team-summary`. Required cases:

1. **Manager sees only direct reports** — seed Manager user M + two IC users (one with `ManagerUserId = M.Id`, one without), insert timesheet lines for both ICs, call `/api/reports/team-summary`, assert only the direct report row appears.
2. **Admin sees all active users** — seed Admin + two users under different managers, call endpoint as Admin, assert all appear.
3. **IC gets 403** — seed IC user, call endpoint, assert `403`.
4. **Finance gets 403** — seed Finance user, call endpoint, assert `403`.
5. **Invalid date format → 400**.
6. **`to < from` → 400**.
7. **Manager with no direct reports → 200 with empty `rows` array**.

**Key seeding note:** To set `ManagerUserId`, insert the manager user first (so their ID exists), then insert the IC user with `ManagerUserId` set. Use direct `db.Users.Add(...)` and `db.SaveChangesAsync()` after resolving the factory's `AppDbContext` via `scope.ServiceProvider`.

### Project Structure — Files to Touch

| File | Action |
|---|---|
| `api/Dtos/ReportDtos.cs` | Add `TeamMemberSummaryRow` + `TeamSummaryResponse` |
| `api/Controllers/ReportsController.cs` | Add `TeamSummary` action |
| `web/src/api.ts` | Add `TeamMemberRow`, `TeamSummary` types + `getTeamSummary` function |
| `web/src/pages/Reports.tsx` | Add `isManagerOrAdmin`, `teamSummary` state, conditional fetch, render team card |
| `tests/C2E.Api.Tests/ReportsApiTests.cs` | Add team-summary test cases (file created by S-E9-01) |

**No migrations needed** — reads existing `AppUser`, `TimesheetLines`, `ExpenseEntries` tables.

### Regression Risk

- S-E9-01 added `detail` state and `getPersonalDetail` fetch to `Reports.tsx`. This story adds a parallel `teamSummary` state. Both are fetched inside the same `load` callback — keep the `loading` boolean shared (single `setLoading(true)` at top / `setLoading(false)` in finally). Do not introduce a second loading state.
- The `isManagerOrAdmin` guard means Finance and Partner users see no change to the Reports page — preserve that.
- Existing `personal-summary` and `personal-detail` endpoints remain untouched.

### `ExpenseStatus` Usage

`ExpenseStatus` is already imported in `ReportsController.cs` (used by `PersonalSummary`). The `SumByStatus` local function pattern is also already established there — copy it directly.

### DisplayName Fallback

`AppUser.DisplayName` can be an empty string (set by `UserProfileName.DefaultFromEmail` on creation but patched to empty edge case). Mirror the guard already in `UsersController.ToResponse`:
```csharp
DisplayName = string.IsNullOrWhiteSpace(m.DisplayName) ? m.Email : m.DisplayName,
```

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-5

### Debug Log References

### Completion Notes List

- Added `team-summary` backend endpoint with `AdminAndManager` RBAC, admin/direct-report member scoping, two bulk range queries (`TimesheetLines`, `ExpenseEntries`), and per-member aggregate rollups sorted by total hours descending.
- Added frontend `getTeamSummary` API parser plus a manager/admin-only Team Report card on `/reports` that uses the shared month range and shared loading cycle.
- Expanded `ReportsApiTests` with team-summary coverage: manager direct reports, admin org scope, IC/Finance forbidden, invalid dates, inverted ranges, and manager-empty-team response.
- Code-review follow-up: added Partner 403 coverage and explicit missing `from`/missing `to` 400 cases for `team-summary`.
- Validation: `dotnet test tests/C2E.Api.Tests/C2E.Api.Tests.csproj --filter FullyQualifiedName~ReportsApiTests` (13 passed), then full `dotnet test` (79 passed). `npm run lint` still fails due to pre-existing `web/src/App.tsx` hook-order issues.

### File List

- `api/Dtos/ReportDtos.cs`
- `api/Controllers/ReportsController.cs`
- `web/src/api.ts`
- `web/src/pages/Reports.tsx`
- `tests/C2E.Api.Tests/ReportsApiTests.cs`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/stories/S-E9-02-manager-team-reports.md`

### Change Log

- 2026-04-21: Implemented S-E9-02 manager/admin team reporting endpoint, UI section, and automated API test coverage.
- 2026-04-21: Code-review patch pass — added Partner/400-missing-param team-summary test coverage.
