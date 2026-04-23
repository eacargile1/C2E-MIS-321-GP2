import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  createQuote,
  downloadExpenseInvoice,
  fetchFinanceExpenseNarrative,
  issueProjectApprovedExpensesInvoice,
  issueProjectPayoutInvoicesByUser,
  listClients,
  listFinanceExpenseLedger,
  listIssuedInvoices,
  listProjects,
  listQuotes,
  openIssuedInvoicePrint,
  type ClientRow,
  type ExpenseRow,
  type IssuedInvoiceListItem,
  type MeProfile,
  type ProjectRow,
  type QuoteRow,
} from '../api'
import '../App.css'

type Toast = { id: number; message: string; variant: 'ok' | 'err' }
const TOAST_MS = 4000
const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })

function statusClass(status: string) {
  const s = status.toLowerCase()
  if (s === 'approved' || s === 'sent' || s === 'accepted') return 'fin-pill fin-pill-pos'
  if (s === 'pending' || s === 'draft') return 'fin-pill fin-pill-warn'
  if (s === 'rejected' || s === 'declined' || s === 'expired') return 'fin-pill fin-pill-neg'
  return 'fin-pill'
}

export default function FinancePage({ token, profile }: { token: string; profile: MeProfile }) {
  const canCreateQuote = profile.role === 'Admin' || profile.role === 'Finance'
  const [ledger, setLedger] = useState<ExpenseRow[]>([])
  const [quotes, setQuotes] = useState<QuoteRow[]>([])
  const [clients, setClients] = useState<ClientRow[]>([])
  const [statusFilter, setStatusFilter] = useState<'All' | ExpenseRow['status']>('All')
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [toasts, setToasts] = useState<Toast[]>([])

  const [qClientId, setQClientId] = useState('')
  const [qTitle, setQTitle] = useState('')
  const [qScope, setQScope] = useState('')
  const [qHours, setQHours] = useState('40')
  const [qRate, setQRate] = useState('')
  const [qValid, setQValid] = useState('')
  const [qStatus, setQStatus] = useState<'Draft' | 'Sent'>('Draft')

  const [invProjects, setInvProjects] = useState<ProjectRow[]>([])
  const [invProjectId, setInvProjectId] = useState('')
  const defaultPeriod = useMemo(() => {
    const t = new Date()
    const y = t.getFullYear()
    const m = String(t.getMonth() + 1).padStart(2, '0')
    const start = `${y}-${m}-01`
    const end = t.toISOString().slice(0, 10)
    return { start, end }
  }, [])
  const [invStart, setInvStart] = useState(defaultPeriod.start)
  const [invEnd, setInvEnd] = useState(defaultPeriod.end)
  const [issued, setIssued] = useState<IssuedInvoiceListItem[]>([])
  const [aiNarrative, setAiNarrative] = useState('')
  const [aiSource, setAiSource] = useState('')

  const pushToast = useCallback((message: string, variant: 'ok' | 'err') => {
    const id = Date.now()
    setToasts((t) => [...t, { id, message, variant }])
    window.setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), TOAST_MS)
  }, [])

  const onDownloadInvoice = useCallback(
    async (id: string) => {
      setBusy(true)
      try {
        await downloadExpenseInvoice(token, id)
      } catch (e) {
        pushToast(e instanceof Error ? e.message : 'Download failed', 'err')
      } finally {
        setBusy(false)
      }
    },
    [pushToast, token],
  )

  const refresh = useCallback(async () => {
    setLoading(true)
    try {
      const [led, qt, cl, pr, iss] = await Promise.all([
        listFinanceExpenseLedger(token),
        listQuotes(token),
        listClients(token, undefined, true),
        listProjects(token),
        listIssuedInvoices(token),
      ])
      setLedger(led)
      setQuotes(qt)
      setClients(cl.filter((c) => c.isActive))
      setInvProjects(pr)
      setIssued(iss)
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Load failed', 'err')
    } finally {
      setLoading(false)
    }
  }, [pushToast, token])

  useEffect(() => {
    void refresh()
  }, [refresh])

  useEffect(() => {
    if (qClientId || clients.length === 0) return
    const first = clients.find((c) => c.isActive) ?? clients[0]
    if (first) setQClientId(first.id)
  }, [clients, qClientId])

  const filteredLedger = useMemo(() => {
    if (statusFilter === 'All') return ledger
    return ledger.filter((r) => r.status === statusFilter)
  }, [ledger, statusFilter])

  const totals = useMemo(() => {
    let pending = 0
    let approved = 0
    let rejected = 0
    for (const r of ledger) {
      if (r.status === 'Pending') pending += r.amount
      else if (r.status === 'Approved') approved += r.amount
      else if (r.status === 'Rejected') rejected += r.amount
    }
    const quotePipeline = quotes.reduce((s, q) => s + q.totalAmount, 0)
    return { pending, approved, rejected, quotePipeline }
  }, [ledger, quotes])

  const onCreateQuote = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!qClientId) {
      pushToast('Select a client', 'err')
      return
    }
    setBusy(true)
    try {
      const hours = Number(qHours)
      const rate = Number(qRate)
      if (!Number.isFinite(hours) || hours <= 0) throw new Error('Hours must be positive')
      if (!Number.isFinite(rate) || rate <= 0) throw new Error('Rate must be positive')
      await createQuote(token, {
        clientId: qClientId,
        title: qTitle.trim(),
        scopeSummary: qScope.trim() || undefined,
        estimatedHours: hours,
        hourlyRate: rate,
        validThrough: qValid.trim() || undefined,
        status: qStatus,
      })
      setQTitle('')
      setQScope('')
      setQHours('40')
      setQRate('')
      setQValid('')
      setQStatus('Draft')
      pushToast('Quote created', 'ok')
      await refresh()
    } catch (e2) {
      pushToast(e2 instanceof Error ? e2.message : 'Create failed', 'err')
    } finally {
      setBusy(false)
    }
  }

  useEffect(() => {
    if (!qClientId) return
    const c = clients.find((x) => x.id === qClientId)
    if (c?.defaultBillingRate != null && qRate === '') setQRate(String(c.defaultBillingRate))
  }, [clients, qClientId, qRate])

  useEffect(() => {
    if (invProjectId || invProjects.length === 0) return
    setInvProjectId(invProjects[0].id)
  }, [invProjectId, invProjects])

  const onIssueProjectExpenses = async () => {
    if (!invProjectId) {
      pushToast('Select a project', 'err')
      return
    }
    setBusy(true)
    try {
      const r = await issueProjectApprovedExpensesInvoice(token, {
        projectId: invProjectId,
        periodStart: invStart,
        periodEnd: invEnd,
      })
      pushToast(`Issued ${r.issueNumber} (${r.lineCount} lines, ${usd.format(r.totalAmount)})`, 'ok')
      setIssued(await listIssuedInvoices(token))
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Issue failed', 'err')
    } finally {
      setBusy(false)
    }
  }

  const onIssuePayouts = async () => {
    if (!invProjectId) {
      pushToast('Select a project', 'err')
      return
    }
    setBusy(true)
    try {
      const list = await issueProjectPayoutInvoicesByUser(token, {
        projectId: invProjectId,
        periodStart: invStart,
        periodEnd: invEnd,
      })
      pushToast(`Created ${list.length} payout invoice(s).`, 'ok')
      setIssued(await listIssuedInvoices(token))
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Issue failed', 'err')
    } finally {
      setBusy(false)
    }
  }

  const onAiNarrative = async () => {
    if (!invProjectId) {
      pushToast('Select a project', 'err')
      return
    }
    setBusy(true)
    try {
      const r = await fetchFinanceExpenseNarrative(token, {
        projectId: invProjectId,
        periodStart: invStart,
        periodEnd: invEnd,
      })
      setAiNarrative(r.narrative)
      setAiSource(r.source)
      pushToast(r.source === 'openai' ? 'Narrative from OpenAI' : 'Heuristic narrative (add API key for OpenAI)', 'ok')
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'AI narrative failed', 'err')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="admin-wrap finance-hub">
      <div className="card admin-card">
        <h1 className="title admin-title">Finance</h1>
        <p className="subtitle admin-sub">
          {profile.displayName} · {profile.role} — expense register, client quotes, and billing context.
        </p>
        <p className="admin-hint" style={{ marginBottom: 0 }}>
          Managers still approve/reject line items; this view is the full financial picture for closed-loop reporting.
        </p>
      </div>

      <section className="dashboard-kpis">
        <article className="card admin-card kpi-card">
          <p className="kpi-label">Pending Expenses</p>
          <p className="kpi-value">{loading ? '--' : usd.format(totals.pending)}</p>
        </article>
        <article className="card admin-card kpi-card">
          <p className="kpi-label">Approved (Register)</p>
          <p className="kpi-value">{loading ? '--' : usd.format(totals.approved)}</p>
        </article>
        <article className="card admin-card kpi-card">
          <p className="kpi-label">Rejected (Register)</p>
          <p className="kpi-value">{loading ? '--' : usd.format(totals.rejected)}</p>
        </article>
        <article className="card admin-card kpi-card">
          <p className="kpi-label">Quoted Pipeline</p>
          <p className="kpi-value">{loading ? '--' : usd.format(totals.quotePipeline)}</p>
        </article>
      </section>

      <div className="card admin-card">
        <h2 className="admin-h2">Issued invoices &amp; AI expense memo</h2>
        <p className="admin-hint">
          Issue documents from <strong>approved</strong> expenses that match the catalog project name and client (same
          rules as project expense insights). You must be <strong>assigned finance</strong> on the project (or Admin).
        </p>
        <div className="form admin-form-grid" style={{ marginTop: '1rem' }}>
          <label className="field">
            <span>Project</span>
            <select value={invProjectId} onChange={(e) => setInvProjectId(e.target.value)} disabled={invProjects.length === 0}>
              {invProjects.length === 0 ? <option value="">No projects visible</option> : null}
              {invProjects.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.clientName} — {p.name}
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            <span>Period start</span>
            <input type="date" value={invStart} onChange={(e) => setInvStart(e.target.value)} />
          </label>
          <label className="field">
            <span>Period end</span>
            <input type="date" value={invEnd} onChange={(e) => setInvEnd(e.target.value)} />
          </label>
          <div className="field" style={{ alignSelf: 'end', display: 'flex', flexWrap: 'wrap', gap: '0.5rem' }}>
            <button type="button" className="btn primary" disabled={busy || !invProjectId} onClick={() => void onIssueProjectExpenses()}>
              Issue project invoice
            </button>
            <button type="button" className="btn secondary" disabled={busy || !invProjectId} onClick={() => void onIssuePayouts()}>
              Issue per-user payout invoices
            </button>
            <button type="button" className="btn secondary" disabled={busy || !invProjectId} onClick={() => void onAiNarrative()}>
              AI expense narrative
            </button>
          </div>
        </div>
        {aiNarrative ? (
          <div style={{ marginTop: '1rem' }}>
            <p className="admin-hint" style={{ marginBottom: '0.35rem' }}>
              Source: <strong>{aiSource}</strong> — aggregates only; review before external use.
            </p>
            <p style={{ whiteSpace: 'pre-wrap' }}>{aiNarrative}</p>
          </div>
        ) : null}
        {issued.length === 0 ? (
          <p className="admin-hint" style={{ marginTop: '1rem' }}>
            No issued invoices yet.
          </p>
        ) : (
          <div className="table-scroll" style={{ marginTop: '1rem' }}>
            <table className="admin-table fin-table">
              <thead>
                <tr>
                  <th>Number</th>
                  <th>Kind</th>
                  <th>Client / Project</th>
                  <th>Payee</th>
                  <th>Period</th>
                  <th>Total</th>
                  <th>Issued (UTC)</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {issued.map((x) => (
                  <tr key={x.id}>
                    <td className="fin-mono">{x.issueNumber}</td>
                    <td>{x.kind}</td>
                    <td>
                      {x.clientName} / {x.projectName}
                    </td>
                    <td>{x.payeeEmail ?? '—'}</td>
                    <td className="fin-mono">
                      {x.periodStart} → {x.periodEnd}
                    </td>
                    <td className="fin-num">{usd.format(x.totalAmount)}</td>
                    <td className="admin-hint">{x.issuedAtUtc.slice(0, 19)}Z</td>
                    <td>
                      <button
                        type="button"
                        className="btn secondary btn-sm"
                        disabled={busy}
                        onClick={() => void openIssuedInvoicePrint(token, x.id).catch((e) => pushToast(e instanceof Error ? e.message : 'Print failed', 'err'))}
                      >
                        Print
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <div className="card admin-card">
        <div className="admin-table-head">
          <h2 className="admin-h2">Expense Register</h2>
          <div className="finance-toolbar">
            <label className="field inline">
              <span>Status</span>
              <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value as typeof statusFilter)}>
                <option value="All">All</option>
                <option value="Pending">Pending</option>
                <option value="Approved">Approved</option>
                <option value="Rejected">Rejected</option>
              </select>
            </label>
            <button type="button" className="btn secondary btn-sm" onClick={() => void refresh()} disabled={loading || busy}>
              Refresh
            </button>
          </div>
        </div>
        {loading ? (
          <p className="admin-hint">Loading…</p>
        ) : filteredLedger.length === 0 ? (
          <p className="admin-hint">No rows for this filter.</p>
        ) : (
          <div className="table-scroll">
            <table className="admin-table fin-table">
              <thead>
                <tr>
                  <th>Submitted By</th>
                  <th>Date</th>
                  <th>Client / Project</th>
                  <th>Category</th>
                  <th>Description</th>
                  <th>Amount</th>
                  <th>Invoice</th>
                  <th>Status</th>
                  <th>Reviewer</th>
                </tr>
              </thead>
              <tbody>
                {filteredLedger.map((r) => (
                  <tr key={r.id}>
                    <td>{r.userEmail}</td>
                    <td>{r.expenseDate}</td>
                    <td>
                      {r.client} / {r.project}
                    </td>
                    <td>{r.category}</td>
                    <td>{r.description}</td>
                    <td className="fin-num">{usd.format(r.amount)}</td>
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
                    <td>
                      <span className={statusClass(r.status)}>{r.status}</span>
                    </td>
                    <td className="admin-hint">{r.reviewedByEmail ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <div className="finance-two-col">
        <div className="card admin-card">
          <h2 className="admin-h2">Client Quotes</h2>
          <p className="admin-hint">Fixed-fee style quotes from estimated hours × rate (snapshot at creation).</p>
          {quotes.length === 0 ? (
            <p className="admin-hint">No quotes yet.</p>
          ) : (
            <div className="table-scroll">
              <table className="admin-table fin-table">
                <thead>
                  <tr>
                    <th>Ref</th>
                    <th>Client</th>
                    <th>Title</th>
                    <th>Hours</th>
                    <th>Rate</th>
                    <th>Total</th>
                    <th>Status</th>
                    <th>Valid</th>
                  </tr>
                </thead>
                <tbody>
                  {quotes.map((q) => (
                    <tr key={q.id}>
                      <td className="fin-mono">{q.referenceNumber}</td>
                      <td>{q.clientName}</td>
                      <td>{q.title}</td>
                      <td className="fin-num">{q.estimatedHours.toFixed(1)}</td>
                      <td className="fin-num">{usd.format(q.hourlyRate)}</td>
                      <td className="fin-num fin-strong">{usd.format(q.totalAmount)}</td>
                      <td>
                        <span className={statusClass(q.status)}>{q.status}</span>
                      </td>
                      <td>{q.validThrough ?? '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>

        {canCreateQuote ? (
          <div className="card admin-card">
            <h2 className="admin-h2">New quote</h2>
            <form className="form admin-form-grid" onSubmit={(e) => void onCreateQuote(e)}>
              <label className="field">
                <span>Client</span>
                <select value={qClientId} onChange={(e) => setQClientId(e.target.value)} required>
                  {clients.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.name}
                      {c.defaultBillingRate != null ? ` · ${usd.format(c.defaultBillingRate)}/hr` : ''}
                    </option>
                  ))}
                </select>
              </label>
              <label className="field">
                <span>Title</span>
                <input
                  value={qTitle}
                  onChange={(e) => setQTitle(e.target.value)}
                  placeholder="e.g. Q2 analytics pilot"
                  required
                />
              </label>
              <label className="field" style={{ gridColumn: '1 / -1' }}>
                <span>Scope summary</span>
                <textarea
                  className="fin-textarea"
                  rows={3}
                  value={qScope}
                  onChange={(e) => setQScope(e.target.value)}
                  placeholder="Deliverables, assumptions, exclusions…"
                />
              </label>
              <label className="field">
                <span>Estimated hours</span>
                <input inputMode="decimal" value={qHours} onChange={(e) => setQHours(e.target.value)} required />
              </label>
              <label className="field">
                <span>Hourly rate ($)</span>
                <input
                  inputMode="decimal"
                  value={qRate}
                  onChange={(e) => setQRate(e.target.value)}
                  placeholder="from client default"
                  required
                />
              </label>
              <label className="field">
                <span>Valid through</span>
                <input type="date" value={qValid} onChange={(e) => setQValid(e.target.value)} />
              </label>
              <label className="field">
                <span>Status</span>
                <select value={qStatus} onChange={(e) => setQStatus(e.target.value as 'Draft' | 'Sent')}>
                  <option value="Draft">Draft</option>
                  <option value="Sent">Sent</option>
                </select>
              </label>
              <button type="submit" className="btn primary" disabled={busy || clients.length === 0}>
                Generate quote
              </button>
            </form>
          </div>
        ) : (
          <div className="card admin-card">
            <h2 className="admin-h2">Quote authoring</h2>
            <p className="admin-hint" style={{ marginBottom: 0 }}>
              Managers can view financial quotes here, but only Admin or Finance can create new quotes.
            </p>
          </div>
        )}
      </div>

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
