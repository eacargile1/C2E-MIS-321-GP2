import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  getTeamSummary,
  getPersonalDetail,
  getPersonalSummary,
  type MeProfile,
  type PersonalDetail,
  type PersonalSummary,
  type TeamSummary,
} from '../api'
import '../App.css'

type Toast = { id: number; message: string; variant: 'ok' | 'err' }
const TOAST_MS = 4000

function pad2(n: number) {
  return String(n).padStart(2, '0')
}

function toYmd(d: Date) {
  return `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`
}

function startOfMonth(d: Date) {
  return new Date(d.getFullYear(), d.getMonth(), 1)
}

function endOfMonth(d: Date) {
  return new Date(d.getFullYear(), d.getMonth() + 1, 0)
}

export default function ReportsPage({ token, profile }: { token: string; profile: MeProfile }) {
  const [anchor, setAnchor] = useState(() => startOfMonth(new Date()))
  const [rangeMode, setRangeMode] = useState<'month' | 'custom'>('month')
  const [customFrom, setCustomFrom] = useState('')
  const [customTo, setCustomTo] = useState('')
  const [appliedCustomFrom, setAppliedCustomFrom] = useState('')
  const [appliedCustomTo, setAppliedCustomTo] = useState('')
  const [customRangeError, setCustomRangeError] = useState<string | null>(null)
  const [summary, setSummary] = useState<PersonalSummary | null>(null)
  const [detail, setDetail] = useState<PersonalDetail | null>(null)
  const [teamSummary, setTeamSummary] = useState<TeamSummary | null>(null)
  const [clientFilter, setClientFilter] = useState('')
  const [projectFilter, setProjectFilter] = useState('')
  const [employeeFilter, setEmployeeFilter] = useState('')
  const [loading, setLoading] = useState(true)
  const [toasts, setToasts] = useState<Toast[]>([])
  const loadSeq = useRef(0)
  const isManagerOrAdmin = profile.role === 'Manager' || profile.role === 'Admin'

  const from = useMemo(() => {
    if (rangeMode === 'custom' && appliedCustomFrom && appliedCustomTo) return appliedCustomFrom
    return toYmd(startOfMonth(anchor))
  }, [rangeMode, appliedCustomFrom, appliedCustomTo, anchor])
  const to = useMemo(() => {
    if (rangeMode === 'custom' && appliedCustomFrom && appliedCustomTo) return appliedCustomTo
    return toYmd(endOfMonth(anchor))
  }, [rangeMode, appliedCustomFrom, appliedCustomTo, anchor])
  const label = useMemo(
    () => anchor.toLocaleDateString(undefined, { month: 'long', year: 'numeric' }),
    [anchor],
  )
  const filteredDetailRows = useMemo(() => {
    if (!detail) return []
    const cf = clientFilter.trim().toLowerCase()
    const pf = projectFilter.trim().toLowerCase()
    return detail.rows.filter(
      (r) => (!cf || r.client.toLowerCase().includes(cf)) && (!pf || r.project.toLowerCase().includes(pf)),
    )
  }, [detail, clientFilter, projectFilter])
  const filteredTeamRows = useMemo(() => {
    if (!teamSummary) return []
    const ef = employeeFilter.trim().toLowerCase()
    if (!ef) return teamSummary.rows
    return teamSummary.rows.filter((r) => r.displayName.toLowerCase().includes(ef) || r.email.toLowerCase().includes(ef))
  }, [teamSummary, employeeFilter])

  const pushToast = useCallback((message: string, variant: 'ok' | 'err') => {
    const id = Date.now()
    setToasts((t) => [...t, { id, message, variant }])
    window.setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), TOAST_MS)
  }, [])

  const load = useCallback(async () => {
    const requestSeq = ++loadSeq.current
    const isCurrent = () => requestSeq === loadSeq.current
    setLoading(true)
    try {
      const s = await getPersonalSummary(token, from, to)
      if (!isCurrent()) return
      setSummary(s)
      try {
        const d = await getPersonalDetail(token, from, to)
        if (!isCurrent()) return
        setDetail(d)
      } catch (e) {
        if (!isCurrent()) return
        setDetail(null)
        pushToast(e instanceof Error ? e.message : 'Load failed', 'err')
      }

      if (isManagerOrAdmin) {
        try {
          const t = await getTeamSummary(token, from, to)
          if (!isCurrent()) return
          setTeamSummary(t)
        } catch (e) {
          if (!isCurrent()) return
          setTeamSummary(null)
          pushToast(e instanceof Error ? e.message : 'Load failed', 'err')
        }
      } else {
        if (!isCurrent()) return
        setTeamSummary(null)
      }
    } catch (e) {
      if (!isCurrent()) return
      setSummary(null)
      setDetail(null)
      setTeamSummary(null)
      pushToast(e instanceof Error ? e.message : 'Load failed', 'err')
    } finally {
      if (isCurrent()) setLoading(false)
    }
  }, [from, isManagerOrAdmin, to, token, pushToast])

  useEffect(() => {
    void load()
  }, [load])

  useEffect(() => {
    setClientFilter('')
    setProjectFilter('')
    setEmployeeFilter('')
  }, [from, to])

  return (
    <div className="admin-wrap">
      <div className="card admin-card">
        <h1 className="title admin-title">My Reports</h1>
        <p className="subtitle admin-sub">
          Signed in as {profile.email} · {profile.role} · personal time + expense rollups
        </p>
      </div>

      <div className="card admin-card">
        <div className="admin-table-head">
          <div>
            <h2 className="admin-h2">Period</h2>
            {rangeMode === 'month' ? (
              <p className="admin-hint">
                {from} → {to} · {label}
              </p>
            ) : (
              <p className="admin-hint">Custom range</p>
            )}
          </div>
          <div className="admin-header-actions">
            {rangeMode === 'month' ? (
              <>
                <button
                  type="button"
                  className="btn secondary btn-sm"
                  onClick={() => setAnchor((d) => new Date(d.getFullYear(), d.getMonth() - 1, 1))}
                  disabled={loading}
                >
                  ← Prev Month
                </button>
                <button
                  type="button"
                  className="btn secondary btn-sm"
                  onClick={() => setAnchor((d) => new Date(d.getFullYear(), d.getMonth() + 1, 1))}
                  disabled={loading}
                >
                  Next Month →
                </button>
                <button type="button" className="btn secondary btn-sm" onClick={() => void load()} disabled={loading}>
                  Refresh
                </button>
                <button
                  type="button"
                  className="btn secondary btn-sm"
                  onClick={() => {
                    setRangeMode('custom')
                    setCustomFrom(from)
                    setCustomTo(to)
                    setCustomRangeError(null)
                  }}
                >
                  Custom Range
                </button>
              </>
            ) : (
              <>
                <label
                  htmlFor="reports-custom-from"
                  className="field"
                  style={{ flexDirection: 'row', alignItems: 'center', gap: 6, margin: 0 }}
                >
                  <span>From</span>
                  <input
                    id="reports-custom-from"
                    type="date"
                    value={customFrom}
                    onChange={(e) => setCustomFrom(e.target.value)}
                    aria-label="From date"
                    style={{ padding: '2px 6px' }}
                  />
                </label>
                <label
                  htmlFor="reports-custom-to"
                  className="field"
                  style={{ flexDirection: 'row', alignItems: 'center', gap: 6, margin: 0 }}
                >
                  <span>To</span>
                  <input
                    id="reports-custom-to"
                    type="date"
                    value={customTo}
                    onChange={(e) => setCustomTo(e.target.value)}
                    aria-label="To date"
                    style={{ padding: '2px 6px' }}
                  />
                </label>
                <button
                  type="button"
                  className="btn secondary btn-sm"
                  disabled={loading}
                  onClick={() => {
                    if (!customFrom || !customTo) return
                    if (customTo < customFrom) {
                      setCustomRangeError('"To" date must be on or after "From" date.')
                      return
                    }
                    setCustomRangeError(null)
                    setAppliedCustomFrom(customFrom)
                    setAppliedCustomTo(customTo)
                  }}
                >
                  Apply
                </button>
                <button
                  type="button"
                  className="btn secondary btn-sm"
                  onClick={() => {
                    setRangeMode('month')
                    setCustomRangeError(null)
                  }}
                >
                  Month View
                </button>
              </>
            )}
          </div>
        </div>
        {customRangeError && (
          <p className="admin-hint" style={{ color: 'var(--danger, #b42318)', marginTop: 4 }}>
            {customRangeError}
          </p>
        )}

        {loading ? (
          <p className="admin-hint">Loading…</p>
        ) : summary ? (
          <div className="form admin-form-grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))' }}>
            <div className="field">
              <span>Total Hours</span>
              <strong>{summary.totalHours.toFixed(2)}</strong>
            </div>
            <div className="field">
              <span>Billable Hours</span>
              <strong>{summary.billableHours.toFixed(2)}</strong>
            </div>
            <div className="field">
              <span>Non-Billable Hours</span>
              <strong>{summary.nonBillableHours.toFixed(2)}</strong>
            </div>
            <div className="field">
              <span>Timesheet Lines</span>
              <strong>{summary.timesheetLineCount}</strong>
            </div>
            <div className="field">
              <span>Expenses (Count)</span>
              <strong>{summary.expenseCount}</strong>
            </div>
            <div className="field">
              <span>Expense Pending $</span>
              <strong>{summary.expensePendingTotal.toFixed(2)}</strong>
            </div>
            <div className="field">
              <span>Expense Approved $</span>
              <strong>{summary.expenseApprovedTotal.toFixed(2)}</strong>
            </div>
            <div className="field">
              <span>Expense Rejected $</span>
              <strong>{summary.expenseRejectedTotal.toFixed(2)}</strong>
            </div>
          </div>
        ) : (
          <p className="admin-hint">No data.</p>
        )}
      </div>

      <div className="card admin-card">
        <h2 className="admin-h2">
          Hours by Client &amp; Project
          {(clientFilter || projectFilter) && detail && detail.rows.length - filteredDetailRows.length > 0 && (
            <span className="admin-hint" style={{ marginLeft: 8, fontWeight: 'normal', fontSize: '0.85em' }}>
              ({detail.rows.length - filteredDetailRows.length} filtered)
            </span>
          )}
        </h2>
        {loading ? (
          <p className="admin-hint">Loading…</p>
        ) : detail ? (
          detail.rows.length > 0 ? (
            <>
              <div style={{ display: 'flex', gap: 8, marginBottom: 8, flexWrap: 'wrap' }}>
                <label
                  htmlFor="reports-client-filter"
                  className="field"
                  style={{ flexDirection: 'row', alignItems: 'center', gap: 6, margin: 0 }}
                >
                  <span>Client</span>
                  <input
                    id="reports-client-filter"
                    type="text"
                    placeholder="Filter client..."
                    value={clientFilter}
                    onChange={(e) => setClientFilter(e.target.value)}
                    aria-label="Filter by client"
                    style={{ padding: '2px 8px' }}
                  />
                </label>
                <label
                  htmlFor="reports-project-filter"
                  className="field"
                  style={{ flexDirection: 'row', alignItems: 'center', gap: 6, margin: 0 }}
                >
                  <span>Project</span>
                  <input
                    id="reports-project-filter"
                    type="text"
                    placeholder="Filter project..."
                    value={projectFilter}
                    onChange={(e) => setProjectFilter(e.target.value)}
                    aria-label="Filter by project"
                    style={{ padding: '2px 8px' }}
                  />
                </label>
              </div>
              {filteredDetailRows.length > 0 ? (
                <table className="admin-table" style={{ width: '100%' }}>
                  <thead>
                    <tr>
                      <th>Client</th>
                      <th>Project</th>
                      <th style={{ textAlign: 'right' }}>Total Hours</th>
                      <th style={{ textAlign: 'right' }}>Billable Hours</th>
                      <th style={{ textAlign: 'right' }}>Non-Billable Hours</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredDetailRows.map((row) => (
                      <tr key={`${row.client}||${row.project}`}>
                        <td>{row.client}</td>
                        <td>{row.project}</td>
                        <td style={{ textAlign: 'right' }}>{row.totalHours.toFixed(2)}</td>
                        <td style={{ textAlign: 'right' }}>{row.billableHours.toFixed(2)}</td>
                        <td style={{ textAlign: 'right' }}>{row.nonBillableHours.toFixed(2)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              ) : (
                <p className="admin-hint">No rows match your filters.</p>
              )}
            </>
          ) : (
            <p className="admin-hint">No timesheet entries in this period.</p>
          )
        ) : (
          <p className="admin-hint">No data.</p>
        )}
      </div>

      {isManagerOrAdmin && (
        <div className="card admin-card">
          <h2 className="admin-h2">
            {profile.role === 'Admin' ? 'All Employees' : 'Direct Reports'} - Team Report
            {employeeFilter && teamSummary && teamSummary.rows.length - filteredTeamRows.length > 0 && (
              <span className="admin-hint" style={{ marginLeft: 8, fontWeight: 'normal', fontSize: '0.85em' }}>
                ({teamSummary.rows.length - filteredTeamRows.length} filtered)
              </span>
            )}
          </h2>
          {loading ? (
            <p className="admin-hint">Loading…</p>
          ) : teamSummary && teamSummary.rows.length > 0 ? (
            <>
              <div style={{ marginBottom: 8 }}>
                <label
                  htmlFor="reports-employee-filter"
                  className="field"
                  style={{ flexDirection: 'row', alignItems: 'center', gap: 6, margin: 0 }}
                >
                  <span>Employee</span>
                  <input
                    id="reports-employee-filter"
                    type="text"
                    placeholder="Filter by name or email..."
                    value={employeeFilter}
                    onChange={(e) => setEmployeeFilter(e.target.value)}
                    aria-label="Filter by employee"
                    style={{ padding: '2px 8px' }}
                  />
                </label>
              </div>
              {filteredTeamRows.length > 0 ? (
                <table className="admin-table" style={{ width: '100%' }}>
                  <thead>
                    <tr>
                      <th>Employee</th>
                      <th>Role</th>
                      <th style={{ textAlign: 'right' }}>Total h</th>
                      <th style={{ textAlign: 'right' }}>Billable h</th>
                      <th style={{ textAlign: 'right' }}>Non-Bill h</th>
                      <th style={{ textAlign: 'right' }}>Timesheet Lines</th>
                      <th style={{ textAlign: 'right' }}>Expenses</th>
                      <th style={{ textAlign: 'right' }}>Pending $</th>
                      <th style={{ textAlign: 'right' }}>Approved $</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredTeamRows.map((row) => (
                      <tr key={row.userId}>
                        <td>{row.displayName}</td>
                        <td>{row.role}</td>
                        <td style={{ textAlign: 'right' }}>{row.totalHours.toFixed(2)}</td>
                        <td style={{ textAlign: 'right' }}>{row.billableHours.toFixed(2)}</td>
                        <td style={{ textAlign: 'right' }}>{row.nonBillableHours.toFixed(2)}</td>
                        <td style={{ textAlign: 'right' }}>{row.timesheetLineCount}</td>
                        <td style={{ textAlign: 'right' }}>{row.expenseCount}</td>
                        <td style={{ textAlign: 'right' }}>{row.expensePendingTotal.toFixed(2)}</td>
                        <td style={{ textAlign: 'right' }}>{row.expenseApprovedTotal.toFixed(2)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              ) : (
                <p className="admin-hint">No rows match your filters.</p>
              )}
            </>
          ) : (
            <p className="admin-hint">
              {profile.role === 'Admin' ? 'No active users found.' : 'No direct reports found.'}
            </p>
          )}
        </div>
      )}

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
