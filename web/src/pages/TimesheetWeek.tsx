import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import {
  getTimesheetWeek,
  listClients,
  listProjects,
  putTimesheetWeek,
  type ClientRow,
  type MeProfile,
  type ProjectRow,
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

function mondayFromSearchParams(sp: URLSearchParams): string | null {
  const w = sp.get('week')
  if (!w || !isYmd(w)) return null
  const [y, m, d] = w.split('-').map(Number)
  const dt = new Date(y!, (m ?? 1) - 1, d ?? 1)
  if (!Number.isFinite(dt.getTime())) return null
  if (dt.getDay() !== 1) return null
  return w
}

function initialWeekStart(): string {
  if (typeof window !== 'undefined') {
    const m = mondayFromSearchParams(new URLSearchParams(window.location.search))
    if (m) return m
  }
  return toYmd(startOfWeekMonday(new Date()))
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

function keyOf(r: { workDate: string; client: string; project: string; task: string }) {
  return `${r.workDate}|${r.client}|${r.project}|${r.task}`
}

export default function TimesheetWeek({
  token,
  profile,
}: {
  token: string
  profile: MeProfile
}) {
  const [searchParams, setSearchParams] = useSearchParams()
  const [weekStart, setWeekStart] = useState(initialWeekStart)
  const [lines, setLines] = useState<DraftLine[]>([])
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [toasts, setToasts] = useState<Toast[]>([])
  const [persistedKeys, setPersistedKeys] = useState<Set<string>>(() => new Set())
  const [catalogClients, setCatalogClients] = useState<ClientRow[]>([])
  const [catalogProjects, setCatalogProjects] = useState<ProjectRow[]>([])
  const lastLoadId = useRef(0)

  const weekStartDate = useMemo(() => {
    if (!isYmd(weekStart)) return startOfWeekMonday(new Date())
    const [y, m, d] = weekStart.split('-').map(Number)
    const dt = new Date(y, (m ?? 1) - 1, d ?? 1)
    return Number.isFinite(dt.getTime()) ? dt : startOfWeekMonday(new Date())
  }, [weekStart])

  const weekEndDate = useMemo(() => addDays(weekStartDate, 6), [weekStartDate])
  const weekHumanLabel = useMemo(() => {
    const opts: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric', year: 'numeric' }
    return `${weekStartDate.toLocaleDateString(undefined, opts)} – ${weekEndDate.toLocaleDateString(undefined, opts)}`
  }, [weekStartDate, weekEndDate])

  const pushToast = useCallback((message: string, variant: 'ok' | 'err') => {
    const id = Date.now()
    setToasts((t) => [...t, { id, message, variant }])
    window.setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), TOAST_MS)
  }, [])

  const setWeekNav = useCallback(
    (ymd: string) => {
      setWeekStart(ymd)
      setSearchParams(
        (prev) => {
          const n = new URLSearchParams(prev)
          n.set('week', ymd)
          return n
        },
        { replace: true },
      )
    },
    [setSearchParams],
  )

  useEffect(() => {
    const m = mondayFromSearchParams(searchParams)
    if (m) setWeekStart(m)
  }, [searchParams])

  const refresh = useCallback(async () => {
    const loadId = ++lastLoadId.current
    setLoading(true)
    try {
      const rows = await getTimesheetWeek(token, weekStart)
      if (loadId !== lastLoadId.current) return
      setPersistedKeys(new Set(rows.map(keyOf)))
      setLines(rows.map(toDraft))
    } catch (e) {
      if (loadId !== lastLoadId.current) return
      pushToast(e instanceof Error ? e.message : 'Load failed', 'err')
      setPersistedKeys(new Set())
      setLines([])
    } finally {
      if (loadId === lastLoadId.current) setLoading(false)
    }
  }, [token, weekStart, pushToast])

  useEffect(() => {
    if (!isYmd(weekStart)) setWeekNav(toYmd(startOfWeekMonday(new Date())))
    void refresh()
  }, [refresh, weekStart, setWeekNav])

  useEffect(() => {
    let cancelled = false
    async function loadCat() {
      try {
        const isAdmin = profile.role === 'Admin'
        const [c, p] = await Promise.all([
          listClients(token, undefined, isAdmin),
          listProjects(token, { includeInactive: isAdmin }),
        ])
        if (!cancelled) {
          setCatalogClients(c)
          setCatalogProjects(p)
        }
      } catch {
        if (!cancelled) {
          setCatalogClients([])
          setCatalogProjects([])
        }
      }
    }
    void loadCat()
    return () => {
      cancelled = true
    }
  }, [token, profile.role])

  const addRow = () => setLines((xs) => [...xs, blankDraft(weekStart)])

  const removeRow = (idx: number) => {
    const r = lines[idx]
    if (!r) return
    const k = keyOf({ workDate: r.workDate, client: r.client.trim(), project: r.project.trim(), task: r.task.trim() })
    if (persistedKeys.has(k) && !isEmptyRow(r)) {
      pushToast('Delete is not supported yet (upsert-only).', 'err')
      return
    }
    setLines((xs) => xs.filter((_, i) => i !== idx))
  }

  const updateRow = (idx: number, patch: Partial<DraftLine>) =>
    setLines((xs) => xs.map((row, i) => (i === idx ? { ...row, ...patch } : row)))

  const setPrevWeek = () => setWeekNav(toYmd(addDays(weekStartDate, -7)))
  const setNextWeek = () => setWeekNav(toYmd(addDays(weekStartDate, 7)))
  const jumpToThisWeek = () => setWeekNav(toYmd(startOfWeekMonday(new Date())))

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
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Save failed', 'err')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="admin-wrap">
      <div className="card admin-card">
        <h1 className="title admin-title">Timesheet</h1>
        <p className="subtitle admin-sub">
          Signed in as {profile.displayName} · {profile.role}
        </p>
        <p className="admin-hint" style={{ marginTop: 8 }}>
          This week: <strong>{weekHumanLabel}</strong> ·{' '}
          <Link to="/resource-tracker" style={{ textDecoration: 'underline' }}>
            Resource tracker
          </Link>{' '}
          for the org month view.
        </p>
        {catalogClients.length > 0 ? (
          <p className="admin-hint" style={{ marginTop: 8 }}>
            Client and project must match active directory entries when your org has clients configured.
          </p>
        ) : null}
      </div>

      <div className="card admin-card">
        <div className="admin-table-head">
          <div>
            <h2 className="admin-h2">Weekly entry</h2>
            <p className="admin-hint">
              Monday–Sunday · {weekStart} → {toYmd(weekEndDate)}
            </p>
          </div>
          <div className="admin-header-actions">
            <button type="button" className="btn secondary btn-sm" onClick={setPrevWeek} disabled={loading || saving}>
              ← Prev week
            </button>
            <button type="button" className="btn secondary btn-sm" onClick={jumpToThisWeek} disabled={loading || saving}>
              This week
            </button>
            <button type="button" className="btn secondary btn-sm" onClick={setNextWeek} disabled={loading || saving}>
              Next week →
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
                        No lines yet for this week. Use Add line below, or open a week from the resource tracker.
                      </td>
                    </tr>
                  ) : null}
                  {lines.map((r, idx) => {
                    const clientOrphan =
                      catalogClients.length > 0 &&
                      r.client.trim().length > 0 &&
                      !catalogClients.some((c) => c.name === r.client && c.isActive)
                    const projectOrphan =
                      catalogProjects.length > 0 &&
                      r.project.trim().length > 0 &&
                      !catalogProjects.some(
                        (p) => p.clientName === r.client && p.name === r.project && p.isActive,
                      )
                    return (
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
                          {catalogClients.length > 0 && !clientOrphan ? (
                            <select
                              className="table-input"
                              value={r.client}
                              onChange={(e) => updateRow(idx, { client: e.target.value, project: '' })}
                              aria-label="Client"
                            >
                              <option value="">— Client —</option>
                              {catalogClients
                                .filter((c) => c.isActive)
                                .map((c) => (
                                  <option key={c.id} value={c.name}>
                                    {c.name}
                                  </option>
                                ))}
                            </select>
                          ) : (
                            <input
                              className="table-input"
                              value={r.client}
                              onChange={(e) => updateRow(idx, { client: e.target.value })}
                              aria-label="Client"
                            />
                          )}
                        </td>
                        <td>
                          {catalogClients.length > 0 && !projectOrphan ? (
                            <select
                              className="table-input"
                              value={r.project}
                              onChange={(e) => updateRow(idx, { project: e.target.value })}
                              aria-label="Project"
                              disabled={!r.client.trim()}
                            >
                              <option value="">— Project —</option>
                              {catalogProjects
                                .filter((p) => p.clientName === r.client && p.isActive)
                                .map((p) => (
                                  <option key={p.id} value={p.name}>
                                    {p.name}
                                  </option>
                                ))}
                            </select>
                          ) : (
                            <input
                              className="table-input"
                              value={r.project}
                              onChange={(e) => updateRow(idx, { project: e.target.value })}
                              aria-label="Project"
                            />
                          )}
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
                    )
                  })}
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
