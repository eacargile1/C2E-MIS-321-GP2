import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { getResourceTrackerMonth, type MeProfile, type ResourceTrackerEmployeeRow } from '../api'
import '../App.css'

type Toast = { id: number; message: string; variant: 'ok' | 'err' }

const TOAST_MS = 4000

function pad2(n: number) {
  return String(n).padStart(2, '0')
}

function toYmd(d: Date) {
  return `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`
}

function startOfWeekMonday(d: Date) {
  const x = new Date(d.getFullYear(), d.getMonth(), d.getDate())
  const day = x.getDay()
  const diff = (day + 6) % 7
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

export default function ResourceTracker({
  token,
  profile,
}: {
  token: string
  profile: MeProfile
}) {
  const nav = useNavigate()
  const canPlan = profile.role === 'Admin' || profile.role === 'Manager'

  const [monthAnchor, setMonthAnchor] = useState(() => startOfMonth(new Date()))
  const [monthRows, setMonthRows] = useState<ResourceTrackerEmployeeRow[]>([])
  const [monthLoading, setMonthLoading] = useState(true)
  const [toasts, setToasts] = useState<Toast[]>([])
  const lastMonthLoadId = useRef(0)

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
    void refreshMonth()
  }, [refreshMonth])

  const setPrevMonth = () => setMonthAnchor((d) => new Date(d.getFullYear(), d.getMonth() - 1, 1))
  const setNextMonth = () => setMonthAnchor((d) => new Date(d.getFullYear(), d.getMonth() + 1, 1))
  const jumpToTodayMonth = () => setMonthAnchor(startOfMonth(new Date()))

  const openWeekForDay = (dayYmd: string) => {
    const weekMonday = toYmd(startOfWeekMonday(new Date(dayYmd)))
    nav(`/timesheet?week=${encodeURIComponent(weekMonday)}`)
  }

  return (
    <div className="admin-wrap">
      <div className="card admin-card">
        <h1 className="title admin-title">Resource tracker</h1>
        <p className="subtitle admin-sub">
          Signed in as {profile.displayName} · {profile.role}
        </p>
        <p className="admin-hint" style={{ marginTop: 8 }}>
          Org-wide view: each cell reflects <strong>hours logged in timesheets</strong> for that day (forecasting data
          will layer on later). Enter your own time on the <Link to="/timesheet">Timesheet</Link> page; click a day cell
          below to jump to that week.
        </p>
        {canPlan ? (
          <p className="admin-hint" style={{ marginTop: 8 }}>
            <strong>Forecasting / plan edits</strong> for managers will attach here once staffing and plan data are
            modeled in the API; today the grid stays read-only for every role.
          </p>
        ) : null}
      </div>

      <div className="card admin-card">
        <div className="admin-table-head">
          <div>
            <h2 className="admin-h2">Monthly availability grid</h2>
            <p className="admin-hint">{monthLabel}</p>
          </div>
          <div className="admin-header-actions">
            <button type="button" className="btn secondary btn-sm" onClick={setPrevMonth} disabled={monthLoading}>
              ← Month
            </button>
            <button type="button" className="btn secondary btn-sm" onClick={setNextMonth} disabled={monthLoading}>
              Month →
            </button>
            <button type="button" className="btn secondary btn-sm" onClick={jumpToTodayMonth} disabled={monthLoading}>
              This month
            </button>
            <button type="button" className="btn secondary btn-sm" onClick={() => void refreshMonth()} disabled={monthLoading}>
              Refresh
            </button>
          </div>
        </div>
        <div className="timesheet-legend">
          <span>
            <i className="lg fully" /> Fully booked
          </span>
          <span>
            <i className="lg soft" /> Soft booked
          </span>
          <span>
            <i className="lg available" /> Available
          </span>
          <span>
            <i className="lg pto" /> PTO
          </span>
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
                      <td key={toYmd(d)} className="status-empty">
                        —
                      </td>
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
                          title={`${row.email} · ${day.date} · ${day.status} · ${day.hours.toFixed(2)}h — click to open timesheet week`}
                          role="button"
                          tabIndex={0}
                          onClick={() => openWeekForDay(day.date)}
                          onKeyDown={(e) => {
                            if (e.key === 'Enter' || e.key === ' ') {
                              e.preventDefault()
                              openWeekForDay(day.date)
                            }
                          }}
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
