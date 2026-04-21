# Story 9.4: Report Filters

Status: done

## Story

As a report user (within my role),
I want to filter my personal breakdown by client/project and filter the team table by employee name, and choose a custom date range instead of only month-by-month navigation,
so that I can narrow the analysis to exactly the data I care about.

## Acceptance Criteria

1. **Custom date range:** The period navigator gains a "Custom range" mode with two date inputs (`from` date, `to` date). The existing month prev/next navigation remains as the default ("Month" mode). Users can toggle between modes. When in custom mode and both dates are valid, fetching re-runs with the custom range; when switching back to Month mode, the anchor-month range resumes.
2. **Custom range validation:** If `to` is before `from`, an inline error message is shown and the fetch does not fire.
3. **Personal breakdown client filter:** A text input above the personal breakdown table filters rows where `row.client` contains the typed text (case-insensitive). Filtering is instant (no new API call — filter the already-fetched `detail.rows` in memory).
4. **Personal breakdown project filter:** A second text input filters `row.project` (case-insensitive). Both client and project filters apply simultaneously (AND logic).
5. **Team table employee filter:** A text input above the team table filters rows where `row.displayName` or `row.email` contains the typed text (case-insensitive). No new API call.
6. **Filter reset on period change:** When the date range changes (month nav or custom input), the client/project/employee filter inputs clear and reset to show all rows.
7. **Active filter count indicator:** When any filter is active, a small "(X filtered)" note appears next to the section heading showing how many rows are hidden.
8. **No new API endpoints** — all filtering for client/project/employee is purely client-side on already-fetched data. The custom date range uses the existing `from`/`to` query params already accepted by all three endpoints (`personal-summary`, `personal-detail`, `team-summary`).
9. All filter inputs are accessible: `<label>` elements with `htmlFor`, `aria-label`, or equivalent.

## Tasks / Subtasks

- [x] **Date range mode toggle** (AC: 1, 2)
  - [x] Add `rangeMode` state: `'month' | 'custom'` initialized to `'month'`
  - [x] Add `customFrom` / `customTo` string states (YYYY-MM-DD)
  - [x] Add toggle button "Custom Range" / "Month View" in the period navigator card
  - [x] When mode is `'custom'`: render two `<input type="date">` fields; derive `from`/`to` from their values
  - [x] Validate `customTo >= customFrom` before triggering fetch; show inline error if not
  - [x] When switching to `'month'`: revert `from`/`to` to anchor-derived values
- [x] **Personal section filters** (AC: 3, 4, 6, 7, 9)
  - [x] Add `clientFilter` / `projectFilter` string states initialized to `''`
  - [x] Add two `<input type="text">` filter fields above the breakdown table
  - [x] Compute `filteredDetailRows` via `useMemo` from `detail.rows`, `clientFilter`, `projectFilter`
  - [x] Reset `clientFilter` / `projectFilter` to `''` when `from`/`to` changes (inside or alongside `load`)
  - [x] Show `(N of M rows)` or `(X hidden)` next to "Hours by Client & Project" heading when filter active
- [x] **Team section filter** (AC: 5, 6, 7, 9)
  - [x] Add `employeeFilter` string state initialized to `''`
  - [x] Add one `<input type="text">` filter field above the team table
  - [x] Compute `filteredTeamRows` via `useMemo` from `teamSummary.rows` and `employeeFilter`
  - [x] Reset `employeeFilter` to `''` when `from`/`to` changes
  - [x] Show row count indicator when filter active
- [x] **No backend changes needed** (AC: 8)

### Review Findings

- [x] [Review][Patch] Show `(X filtered)` indicator only when rows are actually hidden (`X > 0`) to satisfy AC7 wording [web/src/pages/Reports.tsx]
- [x] [Review][Defer] `load()` race risk (stale response can overwrite newer period data) exists in current reports flow and is outside S-E9-04 acceptance scope [web/src/pages/Reports.tsx] — deferred, pre-existing

## Dev Notes

### Critical: Actual Stack

Same warning as S-E9-01 and S-E9-02 — **ignore architecture.md** references to TanStack Query, Zustand, Tailwind. Use plain React hooks, `fetch` in `api.ts`, `App.css` classes.

### This Story Builds Directly on S-E9-01 + S-E9-02

**Prerequisite:** S-E9-01 and S-E9-02 must be implemented first. This story modifies `Reports.tsx` only — no new API endpoints, no DTO changes.

**Files from prior stories now in play in `Reports.tsx`:**
- `summary` + `detail` states (S-E9-01)
- `teamSummary` state, `isManagerOrAdmin` guard (S-E9-02)
- `from`/`to` derived from `anchor` via `useMemo`
- `load` callback triggered by `from`/`to` via `useEffect`

### Date Range Mode: How It Fits the Existing State

The existing state flow is:
```
anchor (Date) → from (string YYYY-MM-DD) → load() → fetch all three endpoints
              → to   (string YYYY-MM-DD) ↗
```

Add a parallel custom range path:
```
rangeMode === 'month'  → from/to derived from anchor (existing behavior, unchanged)
rangeMode === 'custom' → from/to derived from customFrom/customTo inputs
```

The `from`/`to` `useMemo` values are already what trigger `load` via `useEffect([load])`. So the only change is how `from`/`to` are computed:

```typescript
const [rangeMode, setRangeMode] = useState<'month' | 'custom'>('month')
const [customFrom, setCustomFrom] = useState('')
const [customTo, setCustomTo] = useState('')
const [customRangeError, setCustomRangeError] = useState<string | null>(null)

// Replace existing from/to useMemo:
const from = useMemo(() => {
  if (rangeMode === 'custom' && customFrom && customTo) return customFrom
  return toYmd(startOfMonth(anchor))
}, [rangeMode, customFrom, customTo, anchor])

const to = useMemo(() => {
  if (rangeMode === 'custom' && customFrom && customTo) return customTo
  return toYmd(endOfMonth(anchor))
}, [rangeMode, customFrom, customTo, anchor])
```

**Custom range validation** — validate inside `load` before fetching (or gate `from`/`to` derivation):

```typescript
// In load callback, add at top before setLoading:
if (rangeMode === 'custom') {
  if (!customFrom || !customTo) return // don't fetch with incomplete inputs
  if (customTo < customFrom) {
    setCustomRangeError('"To" date must be on or after "From" date.')
    return
  }
}
setCustomRangeError(null)
```

### Period Navigator Card: Augmented JSX

Replace the existing period card content with:

```tsx
<div className="card admin-card">
  <div className="admin-table-head">
    <div>
      <h2 className="admin-h2">Period</h2>
      {rangeMode === 'month' ? (
        <p className="admin-hint">{from} → {to} · {label}</p>
      ) : (
        <p className="admin-hint">Custom range</p>
      )}
    </div>
    <div className="admin-header-actions">
      {rangeMode === 'month' ? (
        <>
          <button type="button" className="btn secondary btn-sm" onClick={() => setAnchor(d => new Date(d.getFullYear(), d.getMonth() - 1, 1))} disabled={loading}>← Prev Month</button>
          <button type="button" className="btn secondary btn-sm" onClick={() => setAnchor(d => new Date(d.getFullYear(), d.getMonth() + 1, 1))} disabled={loading}>Next Month →</button>
          <button type="button" className="btn secondary btn-sm" onClick={() => void load()} disabled={loading}>Refresh</button>
          <button type="button" className="btn secondary btn-sm" onClick={() => { setRangeMode('custom'); setCustomFrom(from); setCustomTo(to) }}>Custom Range</button>
        </>
      ) : (
        <>
          <label className="field" style={{ flexDirection: 'row', alignItems: 'center', gap: 6, margin: 0 }}>
            <span>From</span>
            <input type="date" value={customFrom} onChange={e => setCustomFrom(e.target.value)} style={{ padding: '2px 6px' }} />
          </label>
          <label className="field" style={{ flexDirection: 'row', alignItems: 'center', gap: 6, margin: 0 }}>
            <span>To</span>
            <input type="date" value={customTo} onChange={e => setCustomTo(e.target.value)} style={{ padding: '2px 6px' }} />
          </label>
          <button type="button" className="btn secondary btn-sm" onClick={() => void load()} disabled={loading}>Apply</button>
          <button type="button" className="btn secondary btn-sm" onClick={() => { setRangeMode('month'); setCustomRangeError(null) }}>Month View</button>
        </>
      )}
    </div>
  </div>
  {customRangeError && <p className="admin-hint" style={{ color: 'var(--danger, #b42318)', marginTop: 4 }}>{customRangeError}</p>}
</div>
```

### Personal Breakdown Filters

Add state:
```typescript
const [clientFilter, setClientFilter] = useState('')
const [projectFilter, setProjectFilter] = useState('')
```

Add memoized filtered rows:
```typescript
const filteredDetailRows = useMemo(() => {
  if (!detail) return []
  const cf = clientFilter.trim().toLowerCase()
  const pf = projectFilter.trim().toLowerCase()
  return detail.rows.filter(r =>
    (!cf || r.client.toLowerCase().includes(cf)) &&
    (!pf || r.project.toLowerCase().includes(pf))
  )
}, [detail, clientFilter, projectFilter])
```

Reset filters when period changes — add to the `load` callback before the fetch:
```typescript
setClientFilter('')
setProjectFilter('')
setEmployeeFilter('')
```

Filter inputs above the table (inside the personal breakdown card, before the `<table>`):
```tsx
<div style={{ display: 'flex', gap: 8, marginBottom: 8, flexWrap: 'wrap' }}>
  <label className="field" style={{ flexDirection: 'row', alignItems: 'center', gap: 6, margin: 0 }}>
    <span>Client</span>
    <input
      type="text"
      placeholder="Filter client…"
      value={clientFilter}
      onChange={e => setClientFilter(e.target.value)}
      aria-label="Filter by client"
      style={{ padding: '2px 8px' }}
    />
  </label>
  <label className="field" style={{ flexDirection: 'row', alignItems: 'center', gap: 6, margin: 0 }}>
    <span>Project</span>
    <input
      type="text"
      placeholder="Filter project…"
      value={projectFilter}
      onChange={e => setProjectFilter(e.target.value)}
      aria-label="Filter by project"
      style={{ padding: '2px 8px' }}
    />
  </label>
</div>
```

Section heading with filter count indicator:
```tsx
<h2 className="admin-h2">
  Hours by Client &amp; Project
  {(clientFilter || projectFilter) && detail && (
    <span className="admin-hint" style={{ marginLeft: 8, fontWeight: 'normal', fontSize: '0.85em' }}>
      ({filteredDetailRows.length} of {detail.rows.length} shown)
    </span>
  )}
</h2>
```

Use `filteredDetailRows` instead of `detail.rows` when rendering the table body.

### Team Section Filter

Add state:
```typescript
const [employeeFilter, setEmployeeFilter] = useState('')
```

Memoized filtered rows:
```typescript
const filteredTeamRows = useMemo(() => {
  if (!teamSummary) return []
  const ef = employeeFilter.trim().toLowerCase()
  if (!ef) return teamSummary.rows
  return teamSummary.rows.filter(r =>
    r.displayName.toLowerCase().includes(ef) ||
    r.email.toLowerCase().includes(ef)
  )
}, [teamSummary, employeeFilter])
```

Employee filter input above the team table (inside the team card):
```tsx
<div style={{ marginBottom: 8 }}>
  <label className="field" style={{ flexDirection: 'row', alignItems: 'center', gap: 6, margin: 0 }}>
    <span>Employee</span>
    <input
      type="text"
      placeholder="Filter by name or email…"
      value={employeeFilter}
      onChange={e => setEmployeeFilter(e.target.value)}
      aria-label="Filter by employee"
      style={{ padding: '2px 8px' }}
    />
  </label>
</div>
```

Team heading with filter indicator:
```tsx
<h2 className="admin-h2">
  {profile.role === 'Admin' ? 'All Employees' : 'Direct Reports'} — Team Report
  {employeeFilter && teamSummary && (
    <span className="admin-hint" style={{ marginLeft: 8, fontWeight: 'normal', fontSize: '0.85em' }}>
      ({filteredTeamRows.length} of {teamSummary.rows.length} shown)
    </span>
  )}
</h2>
```

Use `filteredTeamRows` instead of `teamSummary.rows` when rendering the team table body.

### Reset Filters on Period Change

Inside the `load` useCallback, add at the very top (before `setLoading(true)`):
```typescript
setClientFilter('')
setProjectFilter('')
setEmployeeFilter('')
```

This ensures stale filter text doesn't silently hide rows after a period change.

### `useCallback` Dependency Array

After adding the filter resets and the `customRangeError`/`rangeMode` checks to `load`, the dependency array for `useCallback` must include any new state setters used inside it. State setter functions (`setClientFilter`, etc.) are stable references — they do NOT need to be in the dep array. `rangeMode`, `customFrom`, `customTo` ARE used as values inside `load` and must be included:

```typescript
}, [from, to, token, pushToast, rangeMode, customFrom, customTo])
```

### `useMemo` for `from`/`to` — Dependency Inclusion

The new `from`/`to` memos depend on `rangeMode`, `customFrom`, `customTo`, `anchor`. The existing `load` `useEffect` depends on `load` (which depends on `from`, `to`). This chain is correct — changing a custom date input → `from`/`to` recompute → `load` changes identity → `useEffect` re-runs.

**However:** Do NOT auto-fire `load` on every keystroke of the custom date inputs. The `Apply` button should be the trigger. To prevent auto-firing:
- Only include `customFrom`/`customTo` in the `from`/`to` `useMemo` deps if you want auto-fire (not recommended for custom range).
- **Better approach:** Keep `from`/`to` derivation from anchor for Month mode. For custom mode, store a separate `appliedCustomFrom`/`appliedCustomTo` state that only updates when "Apply" is clicked:

```typescript
const [appliedCustomFrom, setAppliedCustomFrom] = useState('')
const [appliedCustomTo, setAppliedCustomTo] = useState('')

// from/to useMemo:
const from = useMemo(() => {
  if (rangeMode === 'custom' && appliedCustomFrom && appliedCustomTo) return appliedCustomFrom
  return toYmd(startOfMonth(anchor))
}, [rangeMode, appliedCustomFrom, appliedCustomTo, anchor])

// "Apply" button onClick:
onClick={() => {
  if (!customFrom || !customTo) return
  if (customTo < customFrom) { setCustomRangeError('"To" must be on or after "From".'); return }
  setCustomRangeError(null)
  setAppliedCustomFrom(customFrom)
  setAppliedCustomTo(customTo)
  // from/to will recompute → load will re-run via useEffect
}}
```

This avoids fetching on every character typed in the date inputs.

### Project Structure — Files to Touch

| File | Action |
|---|---|
| `web/src/pages/Reports.tsx` | All changes — state, memos, filter inputs, JSX |

**No backend files touched. No `api.ts` changes. No migrations.**

### Regression Checklist

- [ ] Month mode still works exactly as before (prev/next/refresh buttons present)
- [ ] Switching to custom and back to month restores month-nav behavior
- [ ] S-E9-01 personal summary KPI tiles unchanged
- [ ] S-E9-01 personal breakdown table still renders when no filters active
- [ ] S-E9-02 team table still renders for Manager/Admin when no filter active
- [ ] Finance/Partner still see no team section
- [ ] Toast stack, loading states, error handling unchanged

### No Tests Required

This story is 100% presentational state management — no new API endpoints, no server-side logic. Unit testing React filter state is low-value for this project's test strategy (which focuses on API integration tests via `WebApplicationFactory`). Skip adding tests for this story.

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-5

### Debug Log References

### Completion Notes List

- Implemented custom date-range UX in `Reports.tsx` with toggleable Month/Custom modes, date inputs, inline custom-range validation, and apply-driven range updates without keystroke-triggered fetches.
- Added client/project in-memory filters for personal breakdown and employee in-memory filter for team table, including active hidden-row indicators and accessible labels.
- Added filter-reset behavior tied to effective period (`from`/`to`) changes so stale filters do not carry into a new period.
- Code-review follow-up: updated filtered-count badge display so it appears only when rows are actually hidden (AC7-compliant).
- Validation: `npx eslint src/pages/Reports.tsx` passed; `dotnet test --filter FullyQualifiedName~ReportsApiTests` remained green (13 passed).

### File List

- `web/src/pages/Reports.tsx`
- `_bmad-output/stories/S-E9-04-report-filters.md`
- `_bmad-output/implementation-artifacts/sprint-status.yaml`

### Change Log

- 2026-04-21: Implemented S-E9-04 report filters and custom range UX in `Reports.tsx` (client-side only, no API changes).
- 2026-04-21: Code-review patch pass — `(X filtered)` indicator now only appears when hidden row count is greater than zero.
