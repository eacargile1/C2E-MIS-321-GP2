import { useCallback, useEffect, useRef, useState } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import {
  approveExpense,
  createExpense,
  downloadExpenseInvoice,
  listClients,
  listMyExpenses,
  listPendingExpenseApprovals,
  listProjects,
  listTeamExpenses,
  rejectExpense,
  reviewExpenseApproverAi,
  reviewExpenseDraftAi,
  type ClientRow,
  type ExpenseRow,
  type MeProfile,
  type OperationsExpenseAiReviewResult,
  type ProjectRow,
} from '../api'
import AiReviewPanel from '../components/AiReviewPanel'
import '../App.css'

type Toast = { id: number; message: string; variant: 'ok' | 'err' }
const TOAST_MS = 4000
const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })

/** Accepts "42.33", "$42.33", "1,234.56" from the amount field. */
function parsePositiveMoneyInput(raw: string): number {
  const s = raw.replace(/\$/g, '').replace(/,/g, '').trim()
  if (!s) throw new Error('Amount is required')
  const parsed = Number(s)
  if (!Number.isFinite(parsed) || parsed <= 0) throw new Error('Amount must be a positive number')
  return parsed
}

function todayYmd() {
  const d = new Date()
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

export default function ExpensesPage({ token, profile }: { token: string; profile: MeProfile }) {
  const location = useLocation()
  const approverAiAnchorRef = useRef<HTMLDivElement | null>(null)
  const canApprove = profile.role === 'Admin' || profile.role === 'Manager' || profile.role === 'Partner'
  const canFinance = profile.role === 'Admin' || profile.role === 'Finance'
  const canSeeTeamExpenseDetail = profile.role === 'Admin' || profile.role === 'Manager'
  const [rows, setRows] = useState<ExpenseRow[]>([])
  const [teamRows, setTeamRows] = useState<ExpenseRow[]>([])
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
  const [invoiceFile, setInvoiceFile] = useState<File | null>(null)
  const [invoiceFieldKey, setInvoiceFieldKey] = useState(0)
  const [catalogClients, setCatalogClients] = useState<ClientRow[]>([])
  const [catalogProjects, setCatalogProjects] = useState<ProjectRow[]>([])
  const [expenseAi, setExpenseAi] = useState<OperationsExpenseAiReviewResult | null>(null)
  const [expenseAiBusy, setExpenseAiBusy] = useState(false)
  const [approverExpenseAi, setApproverExpenseAi] = useState<{
    id: string
    result: OperationsExpenseAiReviewResult
  } | null>(null)
  const [approverExpenseAiBusyId, setApproverExpenseAiBusyId] = useState<string | null>(null)

  const pushToast = useCallback((message: string, variant: 'ok' | 'err') => {
    const id = Date.now()
    setToasts((t) => [...t, { id, message, variant }])
    window.setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), TOAST_MS)
  }, [])

  const refresh = useCallback(async () => {
    setLoading(true)
    try {
      const [mine, team, pend] = await Promise.all([
        listMyExpenses(token),
        canSeeTeamExpenseDetail ? listTeamExpenses(token) : Promise.resolve([] as ExpenseRow[]),
        canApprove ? listPendingExpenseApprovals(token) : Promise.resolve([] as ExpenseRow[]),
      ])
      setRows(mine)
      setTeamRows(team)
      setPending(pend)
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Load failed', 'err')
    } finally {
      setLoading(false)
    }
  }, [canApprove, canSeeTeamExpenseDetail, pushToast, token])

  useEffect(() => {
    void refresh()
  }, [refresh])

  useEffect(() => {
    if (location.hash === '#pending-expense-approvals') {
      window.setTimeout(() => {
        document.getElementById('pending-expense-approvals')?.scrollIntoView({ behavior: 'smooth', block: 'start' })
      }, 100)
    }
  }, [location.hash])

  useEffect(() => {
    if (approverExpenseAi)
      approverAiAnchorRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }, [approverExpenseAi])

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
      } catch (e) {
        if (!cancelled) {
          setCatalogClients([])
          setCatalogProjects([])
          pushToast(e instanceof Error ? e.message : 'Could not load client/project directory', 'err')
        }
      }
    }
    void loadCat()
    return () => {
      cancelled = true
    }
  }, [token, profile.role, pushToast])

  const onCreate = async (e: React.FormEvent) => {
    e.preventDefault()
    setBusy(true)
    try {
      const parsed = parsePositiveMoneyInput(amount)
      if (invoiceFile && invoiceFile.size > 5 * 1024 * 1024) throw new Error('Invoice must be 5 MB or smaller')
      await createExpense(
        token,
        {
          expenseDate,
          client: client.trim(),
          project: project.trim(),
          category: category.trim(),
          description: description.trim(),
          amount: parsed,
        },
        invoiceFile,
      )
      setClient('')
      setProject('')
      setCategory('Meals')
      setDescription('')
      setAmount('')
      setInvoiceFile(null)
      setInvoiceFieldKey((k) => k + 1)
      setExpenseAi(null)
      pushToast('Expense submitted for approval', 'ok')
      await refresh()
    } catch (e2) {
      pushToast(e2 instanceof Error ? e2.message : 'Create failed', 'err')
    } finally {
      setBusy(false)
    }
  }

  const onAiReviewPendingExpense = async (expenseId: string) => {
    setApproverExpenseAiBusyId(expenseId)
    try {
      const r = await reviewExpenseApproverAi(token, expenseId)
      setApproverExpenseAi({ id: expenseId, result: r })
    } catch (e2) {
      pushToast(e2 instanceof Error ? e2.message : 'Approver AI failed', 'err')
    } finally {
      setApproverExpenseAiBusyId(null)
    }
  }

  const onAiReviewExpense = async () => {
    setExpenseAiBusy(true)
    try {
      const parsed = parsePositiveMoneyInput(amount)
      const r = await reviewExpenseDraftAi(token, {
        expenseDate,
        client: client.trim(),
        project: project.trim(),
        category: category.trim(),
        description: description.trim(),
        amount: parsed,
        hasInvoiceAttachment: Boolean(invoiceFile),
      })
      setExpenseAi(r)
    } catch (e2) {
      pushToast(e2 instanceof Error ? e2.message : 'AI review failed', 'err')
    } finally {
      setExpenseAiBusy(false)
    }
  }

  const onDownloadInvoice = async (id: string) => {
    setBusy(true)
    try {
      await downloadExpenseInvoice(token, id)
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Download failed', 'err')
    } finally {
      setBusy(false)
    }
  }

  const onReview = async (id: string, action: 'approve' | 'reject') => {
    setBusy(true)
    try {
      if (action === 'approve') await approveExpense(token, id)
      else await rejectExpense(token, id)
      setApproverExpenseAi(null)
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
        {canSeeTeamExpenseDetail ? (
          <p className="admin-hint" style={{ marginTop: 8 }}>
            {profile.role === 'Admin'
              ? 'Team view shows every submitted expense in the org (all statuses).'
              : 'Team view lists full line detail for your direct reports (all clients and statuses).'}
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
                <option value="">— Select Client —</option>
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
                <option value="">— Select Project —</option>
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
          <label className="field">
            <span>Invoice / Receipt (Optional)</span>
            <input
              key={invoiceFieldKey}
              type="file"
              accept=".pdf,image/jpeg,image/png,image/webp"
              onChange={(e) => setInvoiceFile(e.target.files?.[0] ?? null)}
            />
            <span className="admin-hint" style={{ marginTop: 4 }}>
              PDF Or Image · Max 5 MB
            </span>
          </label>
          <div className="admin-header-actions" style={{ gridColumn: '1 / -1' }}>
            <button
              type="button"
              className="btn secondary"
              disabled={busy || expenseAiBusy}
              onClick={() => void onAiReviewExpense()}
            >
              {expenseAiBusy ? 'Reviewing…' : 'Review Draft (AI + Rules)'}
            </button>
            <button type="submit" className="btn primary" disabled={busy || expenseAiBusy}>
              Submit For Approval
            </button>
          </div>
        </form>
        {expenseAi ? (
          <AiReviewPanel
            title="Pre-submit review"
            usedLlm={expenseAi.usedLlm}
            llmNote={expenseAi.llmNote}
            insights={expenseAi.insights}
            questions={expenseAi.questionsForSubmitter}
          />
        ) : null}
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
                  <th>Invoice</th>
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
                    <td>
                      {r.hasInvoice ? (
                        <button
                          type="button"
                          className="btn secondary btn-sm"
                          onClick={() => void onDownloadInvoice(r.id)}
                          disabled={busy}
                        >
                          Download
                        </button>
                      ) : (
                        <span className="admin-hint">—</span>
                      )}
                    </td>
                    <td>{r.status}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {canSeeTeamExpenseDetail ? (
        <div className="card admin-card">
          <h2 className="admin-h2">{profile.role === 'Admin' ? 'Org Expense Lines' : 'Team Expense Detail'}</h2>
          {loading ? (
            <p className="admin-hint">Loading…</p>
          ) : teamRows.length === 0 ? (
            <p className="admin-hint">No team expenses to show.</p>
          ) : (
            <div className="table-scroll">
              <table className="admin-table">
                <thead>
                  <tr>
                    <th>User</th>
                    <th>Date</th>
                    <th>Client / Project</th>
                    <th>Category</th>
                    <th>Description</th>
                    <th>Amount</th>
                    <th>Invoice</th>
                    <th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  {teamRows.map((r) => (
                    <tr key={r.id}>
                      <td>{r.userEmail}</td>
                      <td>{r.expenseDate}</td>
                      <td>
                        {r.client} / {r.project}
                      </td>
                      <td>{r.category}</td>
                      <td>{r.description}</td>
                      <td>{usd.format(r.amount)}</td>
                      <td>
                        {r.hasInvoice ? (
                          <button
                            type="button"
                            className="btn secondary btn-sm"
                            onClick={() => void onDownloadInvoice(r.id)}
                            disabled={busy}
                          >
                            Download
                          </button>
                        ) : (
                          <span className="admin-hint">—</span>
                        )}
                      </td>
                      <td>{r.status}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      ) : null}

      {canApprove ? (
        <div className="card admin-card" id="pending-expense-approvals">
          <h2 className="admin-h2">Pending Team Approvals</h2>
          <p className="admin-hint" style={{ marginTop: 4 }}>
            Approvals use project delivery manager / engagement partner routing (same rules as Approve). AI review opens
            here after you click Review.
          </p>
          {approverExpenseAi ? (
            <div ref={approverAiAnchorRef}>
              <AiReviewPanel
                title={`Approver review · expense ${approverExpenseAi.id.slice(0, 8)}…${approverExpenseAi.result.submitterEmail ? ` · ${approverExpenseAi.result.submitterEmail}` : ''}`}
                usedLlm={approverExpenseAi.result.usedLlm}
                llmNote={approverExpenseAi.result.llmNote}
                insights={approverExpenseAi.result.insights}
                questions={approverExpenseAi.result.questionsForSubmitter}
                questionsHeading="Reviewer checklist / summary"
              />
            </div>
          ) : null}
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
                    <th>Invoice</th>
                    <th>AI</th>
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
                      <td>
                        {r.hasInvoice ? (
                          <button
                            type="button"
                            className="btn secondary btn-sm"
                            onClick={() => void onDownloadInvoice(r.id)}
                            disabled={busy}
                          >
                            View
                          </button>
                        ) : (
                          <span className="admin-hint">—</span>
                        )}
                      </td>
                      <td>
                        <button
                          type="button"
                          className="btn secondary btn-sm"
                          disabled={busy || approverExpenseAiBusyId === r.id}
                          onClick={() => void onAiReviewPendingExpense(r.id)}
                        >
                          {approverExpenseAiBusyId === r.id ? '…' : 'Review'}
                        </button>
                      </td>
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
