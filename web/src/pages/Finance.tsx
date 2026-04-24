import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  auditFinanceLedgerAi,
  createQuote,
  draftFinanceQuoteAi,
  downloadExpenseInvoice,
  fetchFinanceExpenseNarrative,
  issueProjectApprovedExpensesInvoice,
  issueProjectPayoutInvoicesByUser,
  listClients,
  listFinanceExpenseLedger,
  listIssuedInvoices,
  listProjectStaffingUsers,
  listProjects,
  listQuotes,
  openIssuedInvoicePrint,
  type ClientRow,
  type ExpenseRow,
  type FinanceLedgerAuditResult,
  type FinanceQuoteDraftResult,
  type IssuedInvoiceListItem,
  type MeProfile,
  type ProjectRow,
  type ProjectStaffingUserRow,
  type QuoteRow,
} from '../api'
import AiReviewPanel from '../components/AiReviewPanel'
import '../App.css'

type Toast = { id: number; message: string; variant: 'ok' | 'err' }
const TOAST_MS = 4000
const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })

function addDaysIso(days: number): string {
  const d = new Date()
  d.setDate(d.getDate() + days)
  return d.toISOString().slice(0, 10)
}

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
  const [qProjectId, setQProjectId] = useState('')
  const [qStaffUserId, setQStaffUserId] = useState('')
  const [qTitle, setQTitle] = useState('')
  const [qScope, setQScope] = useState('')
  const [qHours, setQHours] = useState('40')
  const [qRate, setQRate] = useState('')
  const [qValid, setQValid] = useState('')
  const [qStatus, setQStatus] = useState<'Draft' | 'Sent'>('Draft')
  const [staffUsers, setStaffUsers] = useState<ProjectStaffingUserRow[]>([])

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
  const [qContextEmployeeEmail, setQContextEmployeeEmail] = useState('')

  const [ledgerAuditEmployee, setLedgerAuditEmployee] = useState('')
  const [ledgerAuditClient, setLedgerAuditClient] = useState('')
  const [ledgerAuditResult, setLedgerAuditResult] = useState<FinanceLedgerAuditResult | null>(null)
  const [ledgerAuditBusy, setLedgerAuditBusy] = useState(false)
  const [quoteDraftResult, setQuoteDraftResult] = useState<FinanceQuoteDraftResult | null>(null)
  const [quoteDraftBusy, setQuoteDraftBusy] = useState(false)

  const ledgerEmployeeOptions = useMemo(() => {
    const s = new Set<string>()
    for (const r of ledger) s.add(r.userEmail)
    return [...s].sort((a, b) => a.localeCompare(b))
  }, [ledger])

  const ledgerClientOptions = useMemo(() => {
    const s = new Set<string>()
    for (const r of ledger) s.add(r.client)
    return [...s].sort((a, b) => a.localeCompare(b))
  }, [ledger])

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
      const [led, qt, cl, pr, iss, staff] = await Promise.all([
        listFinanceExpenseLedger(token),
        listQuotes(token),
        listClients(token, undefined, true),
        listProjects(token),
        listIssuedInvoices(token),
        listProjectStaffingUsers(token),
      ])
      setLedger(led)
      setQuotes(qt)
      setClients(cl.filter((c) => c.isActive))
      setInvProjects(pr)
      setIssued(iss)
      setStaffUsers(staff)
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Load failed', 'err')
    } finally {
      setLoading(false)
    }
  }, [pushToast, token])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const quoteClients = useMemo(() => {
    if (profile.role !== 'Finance') return clients
    return clients.filter((c) => c.financePortfolioMember === true)
  }, [clients, profile.role])

  useEffect(() => {
    if (qClientId || quoteClients.length === 0) return
    const first = quoteClients.find((c) => c.isActive) ?? quoteClients[0]
    if (first) setQClientId(first.id)
  }, [quoteClients, qClientId])

  useEffect(() => {
    if (profile.role !== 'Finance') return
    if (!qClientId) return
    if (!quoteClients.some((c) => c.id === qClientId)) setQClientId('')
  }, [profile.role, qClientId, quoteClients])

  const selectedClient = useMemo(
    () => clients.find((c) => c.id === qClientId) ?? null,
    [clients, qClientId],
  )

  /** Expense register rows for the quote client (string match on client name). */
  const ledgerRowsForQuoteClient = useMemo(() => {
    const name = selectedClient?.name
    if (!name) return []
    return ledger.filter((r) => r.client === name)
  }, [ledger, selectedClient?.name])

  const quoteExpenseSnapshot = useMemo(() => {
    let pending = 0
    let approved = 0
    let rejected = 0
    let pendingAmt = 0
    let approvedAmt = 0
    for (const r of ledgerRowsForQuoteClient) {
      if (r.status === 'Pending') {
        pending++
        pendingAmt += r.amount
      } else if (r.status === 'Approved') {
        approved++
        approvedAmt += r.amount
      } else if (r.status === 'Rejected') {
        rejected++
      }
    }
    return { pending, approved, rejected, pendingAmt, approvedAmt }
  }, [ledgerRowsForQuoteClient])

  const projectsForQuoteClient = useMemo(
    () => invProjects.filter((p) => p.clientId === qClientId && p.isActive),
    [invProjects, qClientId],
  )

  const projectsForStaffFilter = useMemo(() => {
    if (!qStaffUserId) return projectsForQuoteClient
    return projectsForQuoteClient.filter((p) => (p.teamMemberUserIds ?? []).includes(qStaffUserId))
  }, [projectsForQuoteClient, qStaffUserId])

  useEffect(() => {
    if (!qProjectId) return
    const pr = invProjects.find((p) => p.id === qProjectId)
    if (!pr || pr.clientId !== qClientId) setQProjectId('')
    else if (!projectsForStaffFilter.some((p) => p.id === qProjectId)) setQProjectId('')
  }, [qClientId, qProjectId, invProjects, projectsForStaffFilter])

  useEffect(() => {
    if (!selectedClient) return
    if (selectedClient.defaultBillingRate != null)
      setQRate(String(selectedClient.defaultBillingRate))
    else setQRate('')
    setQValid((v) => (v.trim() === '' ? addDaysIso(60) : v))
    setQTitle((t) => (t.trim() === '' ? `Fixed-fee quote · ${selectedClient.name}` : t))
  }, [selectedClient?.id, selectedClient?.name, selectedClient?.defaultBillingRate])

  const applyProjectPrefill = useCallback(
    (projectId: string) => {
      setQProjectId(projectId)
      if (!projectId) return
      const pr = invProjects.find((p) => p.id === projectId)
      if (!pr) return
      setQClientId(pr.clientId)
      const c = clients.find((cl) => cl.id === pr.clientId)
      const rate = c?.defaultBillingRate
      if (rate != null && rate > 0) {
        setQRate(String(rate))
        const rawHrs = pr.budgetAmount / rate
        const rounded = Math.round(rawHrs / 8) * 8
        setQHours(String(Math.min(1920, Math.max(8, rounded || 40))))
      } else {
        setQHours('40')
      }
      const staffLine =
        qStaffUserId !== ''
          ? (() => {
              const u = staffUsers.find((s) => s.id === qStaffUserId)
              return u
                ? `\nPrimary contributor context: ${u.displayName} (${u.email}, ${u.role}).`
                : ''
            })()
          : ''
      setQTitle(`Fixed-fee quote · ${pr.name}`)
      setQScope(
        `Engagement: ${pr.name} (${pr.clientName}). Budget envelope: ${usd.format(pr.budgetAmount)}.${staffLine}\n\nAdjust hours, rate, and text to match the negotiated SOW.`,
      )
      setQValid((v) => (v.trim() === '' ? addDaysIso(60) : v))
    },
    [clients, invProjects, qStaffUserId, staffUsers],
  )

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

  const fillHoursFromApprovedRegister = useCallback(() => {
    const rate = Number(qRate)
    if (!Number.isFinite(rate) || rate <= 0) {
      pushToast('Set a positive hourly rate first', 'err')
      return
    }
    if (quoteExpenseSnapshot.approvedAmt <= 0) {
      pushToast('No approved expense total for this client in the register', 'err')
      return
    }
    const hrs = Math.max(0.5, Math.round((quoteExpenseSnapshot.approvedAmt / rate) * 10) / 10)
    setQHours(String(hrs))
    setQScope((s) => {
      const note = `\n\n(Draft hours from approved register: ${usd.format(quoteExpenseSnapshot.approvedAmt)} ÷ ${usd.format(rate)}/hr.)`
      return s.includes('Draft hours from approved register') ? s : s.trim() + note
    })
  }, [pushToast, qRate, quoteExpenseSnapshot.approvedAmt])

  const onLedgerAudit = async () => {
    if (!canCreateQuote) return
    setLedgerAuditBusy(true)
    try {
      const r = await auditFinanceLedgerAi(token, {
        employeeEmailContains: ledgerAuditEmployee.trim() || undefined,
        clientNameContains: ledgerAuditClient.trim() || undefined,
        maxRows: 100,
      })
      setLedgerAuditResult(r)
    } catch (e2) {
      pushToast(e2 instanceof Error ? e2.message : 'Ledger audit failed', 'err')
    } finally {
      setLedgerAuditBusy(false)
    }
  }

  const onQuoteSuggest = async () => {
    if (!canCreateQuote || !qClientId) return
    setQuoteDraftBusy(true)
    try {
      const r = await draftFinanceQuoteAi(token, {
        clientId: qClientId,
        contextEmployeeEmail: qContextEmployeeEmail.trim() || undefined,
      })
      setQuoteDraftResult(r)
      if (r.suggestedTitle?.trim()) setQTitle(r.suggestedTitle.trim())
      if (r.suggestedScopeSummary?.trim()) setQScope(r.suggestedScopeSummary.trim())
      if (r.suggestedHours != null && r.suggestedHours > 0) setQHours(String(r.suggestedHours))
      if (r.suggestedHourlyRate != null && r.suggestedHourlyRate > 0) setQRate(String(r.suggestedHourlyRate))
      if (r.suggestedValidThroughYmd?.trim()) setQValid(r.suggestedValidThroughYmd.trim())
      pushToast('Quote fields updated from AI — review before Generate quote', 'ok')
    } catch (e2) {
      pushToast(e2 instanceof Error ? e2.message : 'Quote AI failed', 'err')
    } finally {
      setQuoteDraftBusy(false)
    }
  }

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
      setQProjectId('')
      setQStaffUserId('')
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
    <div className="admin-wrap finance-hub finance-hub-page">
      <div className="card admin-card">
        <h1 className="title admin-title">Finance</h1>
        <p className="subtitle admin-sub">
          {profile.displayName} · {profile.role} — expense register, client quotes, and billing context.
        </p>
        <p className="admin-hint" style={{ marginBottom: 0 }}>
          Managers still approve/reject line items; this view is the full financial picture for closed-loop reporting.
        </p>
      </div>

      <section className="dashboard-kpis finance-kpis">
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
        <div className="form admin-form-grid finance-invoice-row" style={{ marginTop: '1rem' }}>
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
            {canCreateQuote ? (
              <>
                <label className="field inline">
                  <span>AI · Employee</span>
                  <select value={ledgerAuditEmployee} onChange={(e) => setLedgerAuditEmployee(e.target.value)}>
                    <option value="">All</option>
                    {ledgerEmployeeOptions.map((em) => (
                      <option key={em} value={em}>
                        {em}
                      </option>
                    ))}
                  </select>
                </label>
                <label className="field inline">
                  <span>AI · Client</span>
                  <select value={ledgerAuditClient} onChange={(e) => setLedgerAuditClient(e.target.value)}>
                    <option value="">All</option>
                    {ledgerClientOptions.map((c) => (
                      <option key={c} value={c}>
                        {c}
                      </option>
                    ))}
                  </select>
                </label>
                <button
                  type="button"
                  className="btn secondary btn-sm"
                  disabled={loading || busy || ledgerAuditBusy}
                  onClick={() => void onLedgerAudit()}
                >
                  {ledgerAuditBusy ? 'Auditing…' : 'Audit ledger (AI + rules)'}
                </button>
              </>
            ) : null}
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
        {ledgerAuditResult && canCreateQuote ? (
          <AiReviewPanel
            title={`Ledger audit · ${ledgerAuditResult.rowCount} row(s) · pending ${usd.format(ledgerAuditResult.totalPendingAmount)} · approved ${usd.format(ledgerAuditResult.totalApprovedAmount)}`}
            usedLlm={ledgerAuditResult.usedLlm}
            llmNote={ledgerAuditResult.llmNote}
            insights={ledgerAuditResult.insights}
            questions={ledgerAuditResult.summaryPoints}
            questionsHeading="Summary / follow-ups"
          />
        ) : null}
      </div>

      <div className="finance-quotes-layout">
        <div className="card admin-card finance-quotes-list">
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
          <div className="card admin-card finance-quote-editor">
            <h2 className="admin-h2">New quote</h2>
            <p className="admin-hint finance-quote-lede">
              {profile.role === 'Finance' ? (
                <>
                  Client list is limited to accounts where you are the rostered finance lead or assigned finance on an
                  active project. Rate and validity default from the client; optionally narrow by team member or pre-fill
                  from a project.
                </>
              ) : (
                <>
                  Pick a client — hourly rate and quote validity default from the client record. Optionally narrow by
                  team member and pre-fill from an active project (hours estimated from budget ÷ rate). Everything
                  stays editable.
                </>
              )}
            </p>
            {profile.role === 'Finance' && quoteClients.length === 0 ? (
              <p className="admin-hint" style={{ marginTop: 8 }}>
                No finance portfolio yet — ask a Partner to assign you as finance on a new client or on a project.
              </p>
            ) : null}
            <form className="form finance-quote-form" onSubmit={(e) => void onCreateQuote(e)}>
              <label className="field">
                <span>Client</span>
                <select
                  value={qClientId}
                  onChange={(e) => {
                    setQClientId(e.target.value)
                    setQProjectId('')
                  }}
                  required
                >
                  {quoteClients.length === 0 ? (
                    <option value="">—</option>
                  ) : (
                    quoteClients.map((c) => (
                      <option key={c.id} value={c.id}>
                        {c.name}
                        {c.defaultBillingRate != null ? ` · ${usd.format(c.defaultBillingRate)}/hr` : ''}
                      </option>
                    ))
                  )}
                </select>
                {selectedClient?.defaultBillingRate == null ? (
                  <span className="admin-hint">No default billing rate on file — enter rate manually.</span>
                ) : null}
              </label>
              <label className="field">
                <span>Team member (optional)</span>
                <select
                  value={qStaffUserId}
                  onChange={(e) => {
                    setQStaffUserId(e.target.value)
                    setQProjectId('')
                  }}
                >
                  <option value="">— Any —</option>
                  {[...staffUsers]
                    .sort((a, b) => {
                      if (a.role === 'IC' && b.role !== 'IC') return -1
                      if (b.role === 'IC' && a.role !== 'IC') return 1
                      return a.displayName.localeCompare(b.displayName)
                    })
                    .map((u) => (
                      <option key={u.id} value={u.id}>
                        {u.displayName} · {u.role}
                      </option>
                    ))}
                </select>
                <span className="admin-hint">Filters the project list to engagements that include this person.</span>
              </label>
              <label className="field finance-field-full">
                <span>Pre-fill from project (optional)</span>
                <select
                  value={qProjectId}
                  onChange={(e) => void applyProjectPrefill(e.target.value)}
                  disabled={projectsForStaffFilter.length === 0}
                >
                  <option value="">— None —</option>
                  {projectsForStaffFilter.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.name} · budget {usd.format(p.budgetAmount)}
                    </option>
                  ))}
                </select>
                {projectsForQuoteClient.length === 0 ? (
                  <span className="admin-hint">No active projects for this client yet.</span>
                ) : qStaffUserId && projectsForStaffFilter.length === 0 ? (
                  <span className="admin-hint">No projects for this client include the selected team member.</span>
                ) : null}
              </label>
              {selectedClient ? (
                <div className="admin-hint finance-quote-snapshot finance-field-full">
                  <strong>Expense register ({selectedClient.name}):</strong>{' '}
                  {quoteExpenseSnapshot.approved} approved ({usd.format(quoteExpenseSnapshot.approvedAmt)}),{' '}
                  {quoteExpenseSnapshot.pending} pending ({usd.format(quoteExpenseSnapshot.pendingAmt)}),{' '}
                  {quoteExpenseSnapshot.rejected} rejected — for context when pricing recovery into fixed fees.
                  {quoteExpenseSnapshot.approvedAmt > 0 ? (
                    <span style={{ display: 'block', marginTop: 8 }}>
                      <button
                        type="button"
                        className="btn secondary btn-sm"
                        disabled={busy}
                        onClick={() => fillHoursFromApprovedRegister()}
                      >
                        Set hours from approved register
                      </button>
                    </span>
                  ) : null}
                </div>
              ) : null}
              <label className="field">
                <span>Context employee (optional)</span>
                <select value={qContextEmployeeEmail} onChange={(e) => setQContextEmployeeEmail(e.target.value)}>
                  <option value="">— None —</option>
                  {ledgerEmployeeOptions.map((em) => (
                    <option key={em} value={em}>
                      {em}
                    </option>
                  ))}
                </select>
                <span className="admin-hint" style={{ marginTop: 4 }}>
                  Pulled from people in the expense register; narrows AI context for quote text.
                </span>
              </label>
              <div className="admin-header-actions" style={{ gridColumn: '1 / -1' }}>
                <button
                  type="button"
                  className="btn secondary btn-sm"
                  disabled={busy || quoteDraftBusy || !qClientId}
                  onClick={() => void onQuoteSuggest()}
                >
                  {quoteDraftBusy ? 'Suggesting…' : 'Suggest quote fields (AI)'}
                </button>
              </div>
              {quoteDraftResult && quoteDraftResult.reviewerChecklist.length > 0 ? (
                <div className="admin-hint" style={{ gridColumn: '1 / -1' }}>
                  <strong>Reviewer checklist</strong>
                  <ul style={{ marginTop: 6, paddingLeft: '1.25rem' }}>
                    {quoteDraftResult.reviewerChecklist.map((c, i) => (
                      <li key={i}>{c}</li>
                    ))}
                  </ul>
                </div>
              ) : null}
              <label className="field">
                <span>Title</span>
                <input
                  value={qTitle}
                  onChange={(e) => setQTitle(e.target.value)}
                  placeholder="e.g. Q2 analytics pilot"
                  required
                />
              </label>
              <label className="field finance-field-full">
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
              <div className="finance-field-full finance-quote-actions">
                <button type="submit" className="btn primary" disabled={busy || quoteClients.length === 0}>
                  Generate quote
                </button>
              </div>
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
