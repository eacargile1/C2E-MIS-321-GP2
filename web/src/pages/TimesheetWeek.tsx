import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  getResourceTrackerMonth,
  getTimesheetWeek,
  putTimesheetWeek,
  type MeProfile,
  type ResourceTrackerEmployeeRow,
  type TimesheetLine,
} from '../api'
import '../App.css'

type Toast = { id: number; message: string; variant: 'ok' | 'err' }

const TOAST_MS = 4000

type DraftLine = {
  workDate: string
  client: string
  project: string
  task: string
  hours: string
  isBillable: boolean
  notes: string
}

function pad2(n: number) {
  return String(n).padStart(2, '0')
}

function toYmd(d: Date) {
  return `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`
}

function isYmd(s: string) {
  return /^\d{4}-\d{2}-\d{2}$/.test(s)
}

function startOfWeekMonday(d: Date) {
  const x = new Date(d.getFullYear(), d.getMonth(), d.getDate())
  const day = x.getDay() // 0 Sun ... 6 Sat
  const diff = (day + 6) % 7 // Mon -> 0, Sun -> 6
  x.setDate(x.getDate() - diff)
  return x
}

function addDays(d: Date, days: number) {
  const x = new Date(d.getTime())
  x.setDate(x.getDate() + days)
  return x
}

function startOfMonth(d: Date) {
  return new Date(d.getFullYear(), d.getMonth(), 1)
}

function endOfMonth(d: Date) {
  return new Date(d.getFullYear(), d.getMonth() + 1, 0)
}

function toDraft(l: TimesheetLine): DraftLine {
  return {
    workDate: l.workDate,
    client: l.client,
    project: l.project,
    task: l.task,
    hours: String(l.hours),
    isBillable: l.isBillable,
    notes: l.notes ?? '',
  }
}

function blankDraft(defaultWorkDate: string): DraftLine {
  return {
    workDate: defaultWorkDate,
    client: '',
    project: '',
    task: '',
    hours: '',
    isBillable: true,
    notes: '',
  }
}

function isEmptyRow(r: DraftLine) {
  return (
    r.client.trim().length === 0 &&
    r.project.trim().length === 0 &&
    r.task.trim().length === 0 &&
    r.hours.trim().length === 0 &&
    r.notes.trim().length === 0
  )
}

export default function TimesheetWeek({
  token,
  profile,
}: {
  token: string
  profile: MeProfile
}) {
  const [monthAnchor, setMonthAnchor] = useState(() => startOfMonth(new Date()))
  const [weekStart, setWeekStart] = useState(() => toYmd(startOfWeekMonday(new Date())))
  const [lines, setLines] = useState<DraftLine[]>([])
  const [monthRows, setMonthRows] = useState<ResourceTrackerEmployeeRow[]>([])
  const [monthLoading, setMonthLoading] = useState(true)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [toasts, setToasts] = useState<Toast[]>([])
  const lastLoadId = useRef(0)
  const lastMonthLoadId = useRef(0)

  const weekStartDate = useMemo(() => {
    if (!isYmd(weekStart)) return startOfWeekMonday(new Date())
    const [y, m, d] = weekStart.split('-').map(Number)
    const dt = new Date(y, (m ?? 1) - 1, d ?? 1)
    return Number.isFinite(dt.getTime()) ? dt : startOfWeekMonday(new Date())
  }, [weekStart])

  const weekEndDate = useMemo(() => addDays(weekStartDate, 6), [weekStartDate])
  const monthLabel = useMemo(
    () => monthAnchor.toLocaleDateString(undefined, { month: 'long', year: 'numeric' }),
    [monthAnchor],
  )
  const monthStart = useMemo(() => startOfMonth(monthAnchor), [monthAnchor])
  const monthEnd = useMemo(() => endOfMonth(monthAnchor), [monthAnchor])
  const monthStartYmd = useMemo(() => toYmd(monthStart), [monthStart])
  const monthDays = useMemo(() => {
    const out: Date[] = []
    for (let d = monthStart; d <= monthEnd; d = addDays(d, 1)) out.push(d)
    return out
  }, [monthStart, monthEnd])

  const pushToast = useCallback((message: string, variant: 'ok' | 'err') => {
    const id = Date.now()
    setToasts((t) => [...t, { id, message, variant }])
    window.setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), TOAST_MS)
  }, [])

  const refresh = useCallback(async () => {
    const loadId = ++lastLoadId.current
    setLoading(true)
    try {
      const rows = await getTimesheetWeek(token, weekStart)
      if (loadId !== lastLoadId.current) return
      setLines(rows.map(toDraft))
    } catch (e) {
      if (loadId !== lastLoadId.current) return
      pushToast(e instanceof Error ? e.message : 'Load failed', 'err')
      setLines([])
    } finally {
      if (loadId === lastLoadId.current) setLoading(false)
    }
  }, [token, weekStart, pushToast])

  const refreshMonth = useCallback(async () => {
    const loadId = ++lastMonthLoadId.current
    setMonthLoading(true)
    try {
      const rows = await getResourceTrackerMonth(token, monthStartYmd)
      if (loadId !== lastMonthLoadId.current) return
      setMonthRows(rows)
    } catch (e) {
      if (loadId !== lastMonthLoadId.current) return
      pushToast(e instanceof Error ? e.message : 'Month load failed', 'err')
      setMonthRows([])
    } finally {
      if (loadId === lastMonthLoadId.current) setMonthLoading(false)
    }
  }, [monthStartYmd, token, pushToast])

  useEffect(() => {
    if (!isYmd(weekStart)) setWeekStart(toYmd(startOfWeekMonday(new Date())))
    void refresh()
  }, [refresh, weekStart])

  useEffect(() => {
    void refreshMonth()
  }, [refreshMonth])

  const addRow = () => setLines((xs) => [...xs, blankDraft(weekStart)])

  const removeRow = (idx: number) => {
    setLines((xs) => xs.filter((_, i) => i !== idx))
  }

  const updateRow = (idx: number, patch: Partial<DraftLine>) =>
    setLines((xs) => xs.map((r, i) => (i === idx ? { ...r, ...patch } : r)))

  const setPrevWeek = () => setWeekStart(toYmd(addDays(weekStartDate, -7)))
  const setNextWeek = () => setWeekStart(toYmd(addDays(weekStartDate, 7)))
  const setPrevMonth = () => setMonthAnchor((d) => new Date(d.getFullYear(), d.getMonth() - 1, 1))
  const setNextMonth = () => setMonthAnchor((d) => new Date(d.getFullYear(), d.getMonth() + 1, 1))
  const jumpToToday = () => {
    const now = new Date()
    setMonthAnchor(startOfMonth(now))
    setWeekStart(toYmd(startOfWeekMonday(now)))
  }

  const onSave = async () => {
    setSaving(true)
    try {
      const filtered = lines.filter((r) => !isEmptyRow(r))
      const payload: TimesheetLine[] = filtered.map((r) => {
        const hoursStr = r.hours.trim()
        if (!hoursStr.length) throw new Error('Hours are required')
        const hours = Number(hoursStr)
        if (!Number.isFinite(hours)) throw new Error('Hours must be a number')
        if (hours <= 0 || hours > 24) throw new Error('Hours must be > 0 and <= 24')
        const q = hours * 4
        const qRounded = Math.round(q)
        if (Math.abs(q - qRounded) > 1e-9) throw new Error('Hours must be in 0.25 increments')
        return {
          workDate: r.workDate,
          client: r.client.trim(),
          project: r.project.trim(),
          task: r.task.trim(),
          hours,
          isBillable: r.isBillable,
          notes: r.notes.trim().length ? r.notes.trim() : null,
        }
      })

      await putTimesheetWeek(token, weekStart, payload)
      pushToast('Saved', 'ok')
      await refresh()
      await refreshMonth()
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Save failed', 'err')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="admin-wrap">
      <div className="card admin-card">
        <h1 className="title admin-title">Resource Tracker / Timesheet</h1>
        <p className="subtitle admin-sub">Signed in as {profile.email} · {profile.role}</p>
      </div>

      <div className="card admin-card">
        <div className="admin-table-head">
          <div>
            <h2 className="admin-h2">Monthly Availability Grid</h2>
            <p className="admin-hint">{monthLabel}</p>
          </div>
          <div className="admin-header-actions">
            <button type="button" className="btn secondary btn-sm" onClick={setPrevMonth} disabled={monthLoading || saving}>
              ← Month
            </button>
            <button type="button" className="btn secondary btn-sm" onClick={setNextMonth} disabled={monthLoading || saving}>
              Month →
            </button>
            <button type="button" className="btn secondary btn-sm" onClick={jumpToToday} disabled={monthLoading || saving}>
              Today
            </button>
          </div>
        </div>
        <div className="timesheet-legend">
          <span><i className="lg fully" /> Fully Booked</span>
          <span><i className="lg soft" /> Soft Booked</span>
          <span><i className="lg available" /> Available</span>
          <span><i className="lg pto" /> PTO</span>
        </div>
        {monthLoading ? (
          <p className="admin-hint">Loading month…</p>
        ) : (
          <div className="table-scroll">
            <table className="resource-matrix">
              <thead>
                <tr>
                  <th className="sticky-col">Employee</th>
                  {monthDays.map((d) => (
                    <th key={toYmd(d)}>{d.getDate()}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {monthRows.length === 0 ? (
                  <tr>
                    <td className="sticky-col">No employees</td>
                    {monthDays.map((d) => (
                      <td key={toYmd(d)} className="status-empty">—</td>
                    ))}
                  </tr>
                ) : (
                  monthRows.map((row) => (
                    <tr key={row.userId}>
                      <td className="sticky-col" title={`${row.email} (${row.role})`}>
                        {row.email}
                      </td>
                      {row.days.map((day) => (
                        <td
                          key={day.date}
                          className={`status-${day.status}`}
                          title={`${row.email} · ${day.date} · ${day.status} · ${day.hours.toFixed(2)}h`}
                          onClick={() => setWeekStart(toYmd(startOfWeekMonday(new Date(day.date))))}
                        >
                          {day.hours > 0 ? day.hours.toFixed(0) : ''}
                        </td>
                      ))}
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <div className="card admin-card">
        <div className="admin-table-head">
          <div>
            <h2 className="admin-h2">Weekly Editor</h2>
            <p className="admin-hint">
              Monday–Sunday · {weekStart} → {toYmd(weekEndDate)}
            </p>
          </div>
          <div className="admin-header-actions">
            <button type="button" className="btn secondary btn-sm" onClick={setPrevWeek} disabled={loading || saving}>
              ← Prev
            </button>
            <button type="button" className="btn secondary btn-sm" onClick={setNextWeek} disabled={loading || saving}>
              Next →
            </button>
            <button type="button" className="btn secondary btn-sm" onClick={() => void refresh()} disabled={loading || saving}>
              Refresh
            </button>
          </div>
        </div>

        {loading ? (
          <p className="admin-hint">Loading…</p>
        ) : (
          <>
            <div className="table-scroll">
              <table className="admin-table">
                <thead>
                  <tr>
                    <th>Date</th>
                    <th>Client</th>
                    <th>Project</th>
                    <th>Task</th>
                    <th>Hours</th>
                    <th>Billable</th>
                    <th>Notes</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {lines.length === 0 ? (
                    <tr>
                      <td colSpan={8} className="admin-hint">
                        No lines yet for this week. Use Add line below, or pick a week from the calendar above.
                      </td>
                    </tr>
                  ) : null}
                  {lines.map((r, idx) => (
                    <tr key={idx}>
                      <td>
                        <input
                          className="table-input"
                          type="date"
                          value={r.workDate}
                          min={weekStart}
                          max={toYmd(weekEndDate)}
                          onChange={(e) => updateRow(idx, { workDate: e.target.value })}
                          aria-label="Work date"
                        />
                      </td>
                      <td>
                        <input
                          className="table-input"
                          value={r.client}
                          onChange={(e) => updateRow(idx, { client: e.target.value })}
                          aria-label="Client"
                        />
                      </td>
                      <td>
                        <input
                          className="table-input"
                          value={r.project}
                          onChange={(e) => updateRow(idx, { project: e.target.value })}
                          aria-label="Project"
                        />
                      </td>
                      <td>
                        <input
                          className="table-input"
                          value={r.task}
                          onChange={(e) => updateRow(idx, { task: e.target.value })}
                          aria-label="Task"
                        />
                      </td>
                      <td>
                        <input
                          className="table-input"
                          inputMode="decimal"
                          value={r.hours}
                          onChange={(e) => updateRow(idx, { hours: e.target.value })}
                          aria-label="Hours"
                          placeholder="e.g. 1.25"
                        />
                      </td>
                      <td>
                        <input
                          type="checkbox"
                          checked={r.isBillable}
                          onChange={(e) => updateRow(idx, { isBillable: e.target.checked })}
                          aria-label="Billable"
                        />
                      </td>
                      <td>
                        <input
                          className="table-input"
                          value={r.notes}
                          onChange={(e) => updateRow(idx, { notes: e.target.value })}
                          aria-label="Notes"
                        />
                      </td>
                      <td className="admin-actions">
                        <button type="button" className="btn secondary btn-sm" onClick={() => removeRow(idx)} disabled={saving}>
                          Remove
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="admin-header-actions" style={{ marginTop: '0.75rem' }}>
              <button type="button" className="btn secondary btn-sm" onClick={addRow} disabled={saving}>
                Add line
              </button>
              <button type="button" className="btn primary btn-sm" onClick={() => void onSave()} disabled={saving}>
                {saving ? 'Saving…' : 'Save'}
              </button>
            </div>
          </>
        )}
      </div>

      <div className="toast-stack" aria-live="polite">
        {toasts.map((t) => (
          <div key={t.id} className={`toast toast-${t.variant}`}>
            {t.message}
          </div>
        ))}
      </div>
    </div>
  )
}

