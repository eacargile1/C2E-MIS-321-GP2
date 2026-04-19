import { useCallback, useEffect, useState } from 'react'
import { NavLink } from 'react-router-dom'
import {
  approveExpense,
  createExpense,
  listClients,
  listMyExpenses,
  listPendingExpenseApprovals,
  listProjects,
  rejectExpense,
  type ClientRow,
  type ExpenseRow,
  type MeProfile,
  type ProjectRow,
} from '../api'
import '../App.css'

type Toast = { id: number; message: string; variant: 'ok' | 'err' }
const TOAST_MS = 4000
const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })

function todayYmd() {
  const d = new Date()
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

export default function ExpensesPage({ token, profile }: { token: string; profile: MeProfile }) {
  const canApprove = profile.role === 'Admin' || profile.role === 'Manager'
  const canFinance = profile.role === 'Admin' || profile.role === 'Finance'
  const [rows, setRows] = useState<ExpenseRow[]>([])
  const [pending, setPending] = useState<ExpenseRow[]>([])
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [toasts, setToasts] = useState<Toast[]>([])

  const [expenseDate, setExpenseDate] = useState(todayYmd())
  const [client, setClient] = useState('')
  const [project, setProject] = useState('')
  const [category, setCategory] = useState('Meals')
  const [description, setDescription] = useState('')
  const [amount, setAmount] = useState('')
  const [catalogClients, setCatalogClients] = useState<ClientRow[]>([])
  const [catalogProjects, setCatalogProjects] = useState<ProjectRow[]>([])

  const pushToast = useCallback((message: string, variant: 'ok' | 'err') => {
    const id = Date.now()
    setToasts((t) => [...t, { id, message, variant }])
    window.setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), TOAST_MS)
  }, [])

  const refresh = useCallback(async () => {
    setLoading(true)
    try {
      const mine = await listMyExpenses(token)
      setRows(mine)
      if (canApprove) {
        const pend = await listPendingExpenseApprovals(token)
        setPending(pend)
      } else {
        setPending([])
      }
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Load failed', 'err')
    } finally {
      setLoading(false)
    }
  }, [canApprove, pushToast, token])

  useEffect(() => {
    void refresh()
  }, [refresh])

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

  const onCreate = async (e: React.FormEvent) => {
    e.preventDefault()
    setBusy(true)
    try {
      const parsed = Number(amount)
      if (!Number.isFinite(parsed) || parsed <= 0) throw new Error('Amount must be a positive number')
      await createExpense(token, {
        expenseDate,
        client: client.trim(),
        project: project.trim(),
        category: category.trim(),
        description: description.trim(),
        amount: parsed,
      })
      setClient('')
      setProject('')
      setCategory('Meals')
      setDescription('')
      setAmount('')
      pushToast('Expense submitted for approval', 'ok')
      await refresh()
    } catch (e2) {
      pushToast(e2 instanceof Error ? e2.message : 'Create failed', 'err')
    } finally {
      setBusy(false)
    }
  }

  const onReview = async (id: string, action: 'approve' | 'reject') => {
    setBusy(true)
    try {
      if (action === 'approve') await approveExpense(token, id)
      else await rejectExpense(token, id)
      pushToast(action === 'approve' ? 'Expense approved' : 'Expense rejected', 'ok')
      await refresh()
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Review failed', 'err')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="admin-wrap">
      <div className="card admin-card">
        <h1 className="title admin-title">Expenses</h1>
        <p className="subtitle admin-sub">Signed in as {profile.email} · {profile.role}</p>
        {catalogClients.length > 0 ? (
          <p className="admin-hint" style={{ marginTop: 8 }}>
            Client and project must match active directory entries when clients exist in the system.
          </p>
        ) : null}
        {canFinance ? (
          <p className="admin-hint" style={{ marginTop: 8 }}>
            Org-wide expense register and client quotes: <NavLink to="/finance">Finance</NavLink>.
          </p>
        ) : null}
      </div>

      <div className="card admin-card">
        <h2 className="admin-h2">Track Expense</h2>
        <form className="form admin-form-grid" onSubmit={onCreate}>
          <label className="field">
            <span>Date</span>
            <input type="date" value={expenseDate} onChange={(e) => setExpenseDate(e.target.value)} required />
          </label>
          <label className="field">
            <span>Client</span>
            {catalogClients.length > 0 &&
            (!client.trim() || catalogClients.some((c) => c.name === client && c.isActive)) ? (
              <select
                value={client}
                onChange={(e) => {
                  setClient(e.target.value)
                  setProject('')
                }}
                required
              >
                <option value="">— Select client —</option>
                {catalogClients
                  .filter((c) => c.isActive)
                  .map((c) => (
                    <option key={c.id} value={c.name}>
                      {c.name}
                    </option>
                  ))}
              </select>
            ) : (
              <input value={client} onChange={(e) => setClient(e.target.value)} required />
            )}
          </label>
          <label className="field">
            <span>Project</span>
            {catalogClients.length > 0 &&
            (!project.trim() || catalogProjects.some((p) => p.clientName === client && p.name === project && p.isActive)) ? (
              <select value={project} onChange={(e) => setProject(e.target.value)} required disabled={!client.trim()}>
                <option value="">— Select project —</option>
                {catalogProjects
                  .filter((p) => p.clientName === client && p.isActive)
                  .map((p) => (
                    <option key={p.id} value={p.name}>
                      {p.name}
                    </option>
                  ))}
              </select>
            ) : (
              <input value={project} onChange={(e) => setProject(e.target.value)} required />
            )}
          </label>
          <label className="field">
            <span>Category</span>
            <input value={category} onChange={(e) => setCategory(e.target.value)} required />
          </label>
          <label className="field">
            <span>Description</span>
            <input value={description} onChange={(e) => setDescription(e.target.value)} required />
          </label>
          <label className="field">
            <span>Amount</span>
            <input inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} placeholder="e.g. 42.33" required />
          </label>
          <button type="submit" className="btn primary" disabled={busy}>
            Submit for approval
          </button>
        </form>
      </div>

      <div className="card admin-card">
        <div className="admin-table-head">
          <h2 className="admin-h2">My Expenses</h2>
          <button type="button" className="btn secondary btn-sm" onClick={() => void refresh()} disabled={loading || busy}>
            Refresh
          </button>
        </div>
        {loading ? (
          <p className="admin-hint">Loading…</p>
        ) : rows.length === 0 ? (
          <p className="admin-hint">No expenses yet.</p>
        ) : (
          <div className="table-scroll">
            <table className="admin-table">
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Client / Project</th>
                  <th>Category</th>
                  <th>Description</th>
                  <th>Amount</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <tr key={r.id}>
                    <td>{r.expenseDate}</td>
                    <td>{r.client} / {r.project}</td>
                    <td>{r.category}</td>
                    <td>{r.description}</td>
                    <td>{usd.format(r.amount)}</td>
                    <td>{r.status}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {canApprove ? (
        <div className="card admin-card">
          <h2 className="admin-h2">Pending Team Approvals</h2>
          {pending.length === 0 ? (
            <p className="admin-hint">No pending approvals.</p>
          ) : (
            <div className="table-scroll">
              <table className="admin-table">
                <thead>
                  <tr>
                    <th>User</th>
                    <th>Date</th>
                    <th>Client / Project</th>
                    <th>Description</th>
                    <th>Amount</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {pending.map((r) => (
                    <tr key={r.id}>
                      <td>{r.userEmail}</td>
                      <td>{r.expenseDate}</td>
                      <td>{r.client} / {r.project}</td>
                      <td>{r.description}</td>
                      <td>{usd.format(r.amount)}</td>
                      <td className="admin-actions">
                        <button type="button" className="btn primary btn-sm" onClick={() => void onReview(r.id, 'approve')} disabled={busy}>
                          Approve
                        </button>{' '}
                        <button type="button" className="btn secondary btn-sm" onClick={() => void onReview(r.id, 'reject')} disabled={busy}>
                          Reject
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      ) : null}

      <div className="toast-stack" aria-live="polite">
        {toasts.map((t) => (
          <div key={t.id} className={t.variant === 'ok' ? 'toast ok' : 'toast err'}>
            {t.message}
          </div>
        ))}
      </div>
    </div>
  )
}
