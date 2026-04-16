import { useCallback, useEffect, useMemo, useState } from 'react'
import { getPersonalSummary, type MeProfile, type PersonalSummary } from '../api'
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
  const [summary, setSummary] = useState<PersonalSummary | null>(null)
  const [loading, setLoading] = useState(true)
  const [toasts, setToasts] = useState<Toast[]>([])

  const from = useMemo(() => toYmd(startOfMonth(anchor)), [anchor])
  const to = useMemo(() => toYmd(endOfMonth(anchor)), [anchor])
  const label = useMemo(
    () => anchor.toLocaleDateString(undefined, { month: 'long', year: 'numeric' }),
    [anchor],
  )

  const pushToast = useCallback((message: string, variant: 'ok' | 'err') => {
    const id = Date.now()
    setToasts((t) => [...t, { id, message, variant }])
    window.setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), TOAST_MS)
  }, [])

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const s = await getPersonalSummary(token, from, to)
      setSummary(s)
    } catch (e) {
      setSummary(null)
      pushToast(e instanceof Error ? e.message : 'Load failed', 'err')
    } finally {
      setLoading(false)
    }
  }, [from, to, token, pushToast])

  useEffect(() => {
    void load()
  }, [load])

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
            <p className="admin-hint">
              {from} → {to} · {label}
            </p>
          </div>
          <div className="admin-header-actions">
            <button
              type="button"
              className="btn secondary btn-sm"
              onClick={() => setAnchor((d) => new Date(d.getFullYear(), d.getMonth() - 1, 1))}
              disabled={loading}
            >
              ← Prev month
            </button>
            <button
              type="button"
              className="btn secondary btn-sm"
              onClick={() => setAnchor((d) => new Date(d.getFullYear(), d.getMonth() + 1, 1))}
              disabled={loading}
            >
              Next month →
            </button>
            <button type="button" className="btn secondary btn-sm" onClick={() => void load()} disabled={loading}>
              Refresh
            </button>
          </div>
        </div>

        {loading ? (
          <p className="admin-hint">Loading…</p>
        ) : summary ? (
          <div className="form admin-form-grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))' }}>
            <div className="field">
              <span>Total hours</span>
              <strong>{summary.totalHours.toFixed(2)}</strong>
            </div>
            <div className="field">
              <span>Billable hours</span>
              <strong>{summary.billableHours.toFixed(2)}</strong>
            </div>
            <div className="field">
              <span>Non-billable hours</span>
              <strong>{summary.nonBillableHours.toFixed(2)}</strong>
            </div>
            <div className="field">
              <span>Timesheet lines</span>
              <strong>{summary.timesheetLineCount}</strong>
            </div>
            <div className="field">
              <span>Expenses (count)</span>
              <strong>{summary.expenseCount}</strong>
            </div>
            <div className="field">
              <span>Expense pending $</span>
              <strong>{summary.expensePendingTotal.toFixed(2)}</strong>
            </div>
            <div className="field">
              <span>Expense approved $</span>
              <strong>{summary.expenseApprovedTotal.toFixed(2)}</strong>
            </div>
            <div className="field">
              <span>Expense rejected $</span>
              <strong>{summary.expenseRejectedTotal.toFixed(2)}</strong>
            </div>
          </div>
        ) : (
          <p className="admin-hint">No data.</p>
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
