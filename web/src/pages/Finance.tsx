import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  createQuote,
  listClients,
  listFinanceExpenseLedger,
  listQuotes,
  type ClientRow,
  type ExpenseRow,
  type MeProfile,
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

  const pushToast = useCallback((message: string, variant: 'ok' | 'err') => {
    const id = Date.now()
    setToasts((t) => [...t, { id, message, variant }])
    window.setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), TOAST_MS)
  }, [])

  const refresh = useCallback(async () => {
    setLoading(true)
    try {
      const [led, qt, cl] = await Promise.all([
        listFinanceExpenseLedger(token),
        listQuotes(token),
        listClients(token, undefined, true),
      ])
      setLedger(led)
      setQuotes(qt)
      setClients(cl.filter((c) => c.isActive))
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
          <p className="kpi-label">Pending expenses</p>
          <p className="kpi-value">{loading ? '--' : usd.format(totals.pending)}</p>
        </article>
        <article className="card admin-card kpi-card">
          <p className="kpi-label">Approved (register)</p>
          <p className="kpi-value">{loading ? '--' : usd.format(totals.approved)}</p>
        </article>
        <article className="card admin-card kpi-card">
          <p className="kpi-label">Rejected (register)</p>
          <p className="kpi-value">{loading ? '--' : usd.format(totals.rejected)}</p>
        </article>
        <article className="card admin-card kpi-card">
          <p className="kpi-label">Quoted pipeline</p>
          <p className="kpi-value">{loading ? '--' : usd.format(totals.quotePipeline)}</p>
        </article>
      </section>

      <div className="card admin-card">
        <div className="admin-table-head">
          <h2 className="admin-h2">Expense register</h2>
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
                  <th>Submitted by</th>
                  <th>Date</th>
                  <th>Client / Project</th>
                  <th>Category</th>
                  <th>Description</th>
                  <th>Amount</th>
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
          <h2 className="admin-h2">Client quotes</h2>
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
              <input value={qTitle} onChange={(e) => setQTitle(e.target.value)} placeholder="e.g. Q2 analytics pilot" required />
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
              <input inputMode="decimal" value={qRate} onChange={(e) => setQRate(e.target.value)} placeholder="from client default" required />
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
