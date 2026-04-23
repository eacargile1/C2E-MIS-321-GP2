import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import {
  approveTimesheetWeek,
  getPendingTimesheetWeekForReview,
  rejectTimesheetWeek,
  reviewTimesheetApproverAi,
  type MeProfile,
  type OperationsTimesheetWeekAiReviewResult,
  type ProjectBudgetBar,
  type TimesheetPendingWeekReview,
} from '../api'
import AiReviewPanel from '../components/AiReviewPanel'
import '../App.css'

function isYmd(s: string) {
  return /^\d{4}-\d{2}-\d{2}$/.test(s)
}

const usd = new Intl.NumberFormat(undefined, { style: 'currency', currency: 'USD', maximumFractionDigits: 0 })

function ProjectBudgetBarBlock({ b }: { b: ProjectBudgetBar }) {
  const after = b.consumedBillableAmount + b.pendingSubmissionBillableAmount
  const budget = b.budgetAmount

  if (!b.catalogMatched) {
    return (
      <div className="budget-bar-card">
        <div className="budget-bar-header">{b.clientName} / {b.projectName}</div>
        <p className="review-muted">
          Not linked to an active directory project. Billable in this week: <strong>{b.pendingBillableHours.toFixed(2)}h</strong>
        </p>
      </div>
    )
  }

  if (budget <= 0) {
    return (
      <div className="budget-bar-card">
        <div className="budget-bar-header">{b.clientName} / {b.projectName}</div>
        <div className="review-stat-grid review-stat-grid-3">
          <div>
            <span className="review-stat-label">Recognized</span>
            <span className="review-stat-value">{usd.format(b.consumedBillableAmount)}</span>
          </div>
          <div>
            <span className="review-stat-label">This Week</span>
            <span className="review-stat-value">{usd.format(b.pendingSubmissionBillableAmount)}</span>
          </div>
          <div>
            <span className="review-stat-label">Rate</span>
            <span className="review-stat-value">{b.defaultHourlyRate != null ? `${usd.format(b.defaultHourlyRate)}/h` : '—'}</span>
          </div>
        </div>
        <p className="review-muted" style={{ marginTop: 6, marginBottom: 0 }}>
          No project budget on file.
        </p>
      </div>
    )
  }

  const over = after > budget
  const remainder = Math.max(0, budget - after)
  const fc = Math.max(0, b.consumedBillableAmount)
  const fp = Math.max(0, b.pendingSubmissionBillableAmount)
  const fr = remainder
  const flexSum = fc + fp + fr || 1

  return (
    <div className={`budget-bar-card${over ? ' budget-bar-card-over' : ''}`}>
      <div className="budget-bar-header">{b.clientName} / {b.projectName}</div>
      <div className="review-stat-grid review-stat-grid-4">
        <div>
          <span className="review-stat-label">Budget</span>
          <span className="review-stat-value">{usd.format(budget)}</span>
        </div>
        <div>
          <span className="review-stat-label">Recognized</span>
          <span className="review-stat-value">{usd.format(b.consumedBillableAmount)}</span>
        </div>
        <div>
          <span className="review-stat-label">This Week</span>
          <span className="review-stat-value">{usd.format(b.pendingSubmissionBillableAmount)}</span>
        </div>
        <div>
          <span className="review-stat-label">After</span>
          <span className={`review-stat-value${over ? ' review-stat-value-warn' : ''}`}>{usd.format(after)}</span>
        </div>
      </div>
      {b.defaultHourlyRate == null ? (
        <p className="review-muted" style={{ marginTop: 4, marginBottom: 6 }}>
          Client has no default rate — dollar amounts are $0 until Finance sets one.
        </p>
      ) : null}
      <div className={`budget-bar-track${over ? ' budget-bar-track-over' : ''}`} aria-hidden>
        <div className="budget-bar-seg budget-bar-consumed" style={{ flex: `${fc / flexSum} 1 0` }} title="Recognized" />
        <div className="budget-bar-seg budget-bar-pending" style={{ flex: `${fp / flexSum} 1 0` }} title="This Week" />
        <div className="budget-bar-seg budget-bar-remainder" style={{ flex: `${fr / flexSum} 1 0` }} title="Headroom" />
      </div>
      <div className="budget-bar-legend-inline">
        <span>
          <i className="budget-legend-swatch budget-bar-consumed" /> Recognized
        </span>
        <span>
          <i className="budget-legend-swatch budget-bar-pending" /> This Week
        </span>
        <span>
          <i className="budget-legend-swatch budget-bar-remainder" /> Headroom
        </span>
      </div>
    </div>
  )
}

export default function TimesheetApprovalReview({
  token,
  profile,
}: {
  token: string
  profile: MeProfile
}) {
  const nav = useNavigate()
  const [searchParams] = useSearchParams()
  const userId = searchParams.get('userId')?.trim() ?? ''
  const week = searchParams.get('week')?.trim() ?? ''

  const [data, setData] = useState<TimesheetPendingWeekReview | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [approverAi, setApproverAi] = useState<OperationsTimesheetWeekAiReviewResult | null>(null)
  const [approverAiBusy, setApproverAiBusy] = useState(false)

  const validParams = useMemo(
    () => userId.length > 0 && isYmd(week),
    [userId, week],
  )

  const load = useCallback(async () => {
    if (!validParams) {
      setLoading(false)
      setError('Missing or invalid link (need userId and week=YYYY-MM-DD).')
      setData(null)
      return
    }
    setLoading(true)
    setError(null)
    try {
      const d = await getPendingTimesheetWeekForReview(token, userId, week)
      setData(d)
      setApproverAi(null)
    } catch (e) {
      setData(null)
      setError(e instanceof Error ? e.message : 'Load failed')
    } finally {
      setLoading(false)
    }
  }, [token, userId, week, validParams])

  useEffect(() => {
    void load()
  }, [load])

  const totalHours = useMemo(
    () => (data ? data.lines.reduce((s, l) => s + l.hours, 0) : 0),
    [data],
  )
  const billableHours = useMemo(
    () => (data ? data.lines.filter((l) => l.isBillable).reduce((s, l) => s + l.hours, 0) : 0),
    [data],
  )

  const submittedLabel = useMemo(() => {
    if (!data) return ''
    const d = new Date(data.submittedAtUtc)
    return d.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })
  }, [data])

  const onApprove = async () => {
    if (!data) return
    setBusy(true)
    setError(null)
    try {
      await approveTimesheetWeek(token, data.userId, data.weekStart)
      nav('/', { replace: true })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Approve failed')
    } finally {
      setBusy(false)
    }
  }

  const onApproverAi = async () => {
    if (!validParams) return
    setApproverAiBusy(true)
    try {
      const r = await reviewTimesheetApproverAi(token, userId, week)
      setApproverAi(r)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'AI review failed')
    } finally {
      setApproverAiBusy(false)
    }
  }

  const onReject = async () => {
    if (!data) return
    if (!window.confirm(`Reject timesheet for ${data.userEmail} (${data.weekStart})?`)) return
    setBusy(true)
    setError(null)
    try {
      await rejectTimesheetWeek(token, data.userId, data.weekStart)
      nav('/', { replace: true })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Reject failed')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="admin-wrap">
      <div className="card admin-card review-page-card">
        <header className="review-page-header">
          <div>
            <h1 className="title admin-title review-page-title">Review Time Tracking</h1>
            <p className="review-page-crumb">
              <Link to="/">Home</Link> · <Link to="/timesheet">Time Tracking</Link> · {profile.displayName}
            </p>
          </div>
        </header>

        {loading ? (
          <p className="review-muted">Loading…</p>
        ) : error ? (
          <p className="review-page-error" role="alert">
            {error}
          </p>
        ) : data ? (
          <>
            <section className="review-hero" aria-label="Submission summary">
              <div className="review-hero-primary">
                <div className="review-hero-email">{data.userEmail}</div>
                <div className="review-hero-sub">
                  Week of <strong>{data.weekStart}</strong>
                  <span className="review-hero-dot"> · </span>
                  Submitted {submittedLabel}
                </div>
              </div>
              <dl className="review-hero-dl">
                <div>
                  <dt>Total</dt>
                  <dd>{totalHours.toFixed(2)}h</dd>
                </div>
                <div>
                  <dt>Billable</dt>
                  <dd>{billableHours.toFixed(2)}h</dd>
                </div>
                <div>
                  <dt>Lines</dt>
                  <dd>{data.lines.length}</dd>
                </div>
              </dl>
            </section>

            {data.projectBudgetBars.length > 0 ? (
              <section className="review-budget-section" aria-label="Budget impact">
                <div className="review-section-head">
                  <h2 className="review-section-title">Budget By Project</h2>
                  <details className="review-budget-help">
                    <summary>How this is calculated</summary>
                    <p>
                      Uses each project&apos;s budget and the client&apos;s default hourly rate (Finance/Admin).
                      <strong> Recognized</strong> = billable time after delivery-manager-approved weeks (IC, Finance) and
                      engagement-partner-approved weeks (Manager, Partner), plus Admin and other non-gated lines.{' '}
                      <strong>This Week</strong> = this submission only.
                    </p>
                  </details>
                </div>
                <div className="review-budget-list">
                  {data.projectBudgetBars.map((b, i) => (
                    <ProjectBudgetBarBlock key={`${b.clientName}-${b.projectName}-${i}`} b={b} />
                  ))}
                </div>
              </section>
            ) : null}

            <section className="review-lines-section" aria-label="Timesheet lines">
              <h2 className="review-section-title">Line Detail</h2>
              <div className="table-scroll">
                <table className="admin-table">
                  <thead>
                    <tr>
                      <th>Date</th>
                      <th>Client</th>
                      <th>Project</th>
                      <th>Task</th>
                      <th>Hrs</th>
                      <th>Bill</th>
                      <th>Notes</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.lines.length === 0 ? (
                      <tr>
                        <td colSpan={7} className="review-muted">
                          No Lines For This Week.
                        </td>
                      </tr>
                    ) : (
                      data.lines.map((l, i) => (
                        <tr key={`${l.workDate}-${l.client}-${l.project}-${l.task}-${i}`}>
                          <td>{l.workDate}</td>
                          <td>{l.client}</td>
                          <td>{l.project}</td>
                          <td>{l.task}</td>
                          <td>{l.hours}</td>
                          <td>{l.isBillable ? 'Y' : '—'}</td>
                          <td className="review-notes-cell">{l.notes?.trim() ? l.notes : '—'}</td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            </section>

            <footer className="review-actions">
              <button
                type="button"
                className="btn secondary"
                disabled={busy || approverAiBusy}
                onClick={() => void onApproverAi()}
              >
                {approverAiBusy ? 'Reviewing…' : 'Reviewer AI check'}
              </button>
              <button type="button" className="btn primary" disabled={busy || approverAiBusy} onClick={() => void onApprove()}>
                Approve Week
              </button>
              <button type="button" className="btn secondary" disabled={busy || approverAiBusy} onClick={() => void onReject()}>
                Reject Week
              </button>
              <button type="button" className="btn secondary btn-sm" disabled={busy} onClick={() => void load()}>
                Refresh
              </button>
            </footer>
            {approverAi ? (
              <AiReviewPanel
                title={`Approver review · ${approverAi.subjectEmail ?? data.userEmail} · week ${data.weekStart}`}
                usedLlm={approverAi.usedLlm}
                llmNote={approverAi.llmNote}
                insights={approverAi.insights}
                questions={approverAi.questionsForEmployee}
                questionsHeading="Reviewer checklist / questions"
                noteSuggestions={approverAi.noteSuggestions}
              />
            ) : null}
          </>
        ) : (
          <p className="review-muted">Nothing to show.</p>
        )}
      </div>
    </div>
  )
}
