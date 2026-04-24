import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  BrowserRouter,
  NavLink,
  Navigate,
  Outlet,
  Route,
  Routes,
  useLocation,
  useNavigate,
} from 'react-router-dom'
import {
  approveTimesheetWeek,
  getTimesheetWeek,
  getTimesheetWeekStatus,
  listClients,
  listMyExpenses,
  listMyPtoRequests,
  listPendingExpenseApprovals,
  listPendingTimesheetWeekApprovals,
  listProjects,
  listQuotes,
  login,
  me,
  rejectTimesheetWeek,
  type ExpenseRow,
  type MeProfile,
  type PendingTimesheetWeek,
  type PtoRequestRow,
  type QuoteRow,
  type TimesheetWeekStatusPayload,
} from './api'
import AdminUsers from './pages/AdminUsers'
import ClientsPage from './pages/Clients'
import ExpensesPage from './pages/Expenses'
import FinancePage from './pages/Finance'
import ProjectDetailPage from './pages/ProjectDetail'
import ProjectsPage from './pages/Projects'
import ReportsPage from './pages/Reports'
import ResourceTracker from './pages/ResourceTracker'
import ResourceTrackerLayout from './pages/ResourceTrackerLayout'
import ResourceTrackerProjectTasks from './pages/ResourceTrackerProjectTasks'
import TimesheetApprovalReview from './pages/TimesheetApprovalReview'
import TimesheetWeek from './pages/TimesheetWeek'
import './App.css'

export type Session = { token: string; profile: MeProfile }

function toYmd(d: Date) {
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

function weekStartMonday(d: Date) {
  const x = new Date(d.getFullYear(), d.getMonth(), d.getDate())
  const offset = (x.getDay() + 6) % 7
  x.setDate(x.getDate() - offset)
  return x
}

function expenseUiStatus(s: ExpenseRow['status']): 'approved' | 'pending' | 'denied' {
  if (s === 'Approved') return 'approved'
  if (s === 'Pending') return 'pending'
  return 'denied'
}

function quoteUiStatus(s: string): 'approved' | 'pending' | 'denied' {
  if (s === 'Accepted') return 'approved'
  if (s === 'Declined' || s === 'Expired') return 'denied'
  return 'pending'
}

function StatusBadge({ variant }: { variant: 'approved' | 'pending' | 'denied' | 'draft' }) {
  const label =
    variant === 'approved'
      ? 'Approved'
      : variant === 'pending'
        ? 'Pending'
        : variant === 'draft'
          ? 'Draft'
          : 'Denied'
  return <span className={`status-badge status-${variant}`}>{label}</span>
}

function timesheetWeekStatusVariant(s: TimesheetWeekStatusPayload['status']): 'approved' | 'pending' | 'denied' | 'draft' {
  if (s === 'Approved') return 'approved'
  if (s === 'Pending') return 'pending'
  if (s === 'Rejected') return 'denied'
  return 'draft'
}

/** Hover copy for “Hours This Week” — weekly sign-off varies by role. */
function hoursThisWeekHelpTooltip(
  role: string,
  weekHours: number,
  snap: {
    loading: boolean
    error: string | null
    myTimesheetWeek: TimesheetWeekStatusPayload | null
  },
): string {
  if (snap.loading) return 'Loading timesheet status…'
  if (snap.error) return snap.error
  const tw = snap.myTimesheetWeek
  if (!tw) return 'Time tracking status not available.'
  const grid = tw.totalHours.toFixed(2)
  const gridBill = tw.billableHours.toFixed(2)
  const sub = (tw.pendingSubmissionTotalHours ?? tw.totalHours).toFixed(2)
  const subBill = (tw.pendingSubmissionBillableHours ?? tw.billableHours).toFixed(2)
  const kpi = weekHours.toFixed(2)

  const usesWeekSignoff =
    role === 'IC' ||
    role === 'Finance' ||
    role === 'Manager' ||
    role === 'Partner' ||
    role === 'Admin'

  if (!usesWeekSignoff) {
    return `You have ${kpi}h logged this week (${gridBill}h billable on lines).`
  }

  if (role === 'Admin') {
    if (tw.status === 'Approved')
      return `Admin weeks self-sign on submit (${gridBill}h billable in the signed week). You have ${kpi}h logged this calendar week on the grid.`
    return `You have ${kpi}h logged this week (${gridBill}h billable). Submit from Time Tracking when you want this week recorded as signed-off.`
  }

  if (tw.status === 'Pending') {
    return `This week is in the approval queue. Reviewers evaluate the submission snapshot: ${sub}h total (${subBill}h billable) — captured when you clicked Submit. Your timesheet grid currently shows ${grid}h total (${gridBill}h billable). If you had an earlier approval and then edited the week, the prior sign-off was cleared and the full week was re-submitted.`
  }
  if (tw.status === 'Approved')
    return `This week is signed off. Latest grid totals: ${grid}h (${gridBill}h billable). KPI uses ${kpi}h from the same week view.`
  if (tw.status === 'Rejected')
    return `Week was rejected — nothing is in the approval queue until you resubmit. Grid: ${grid}h (${gridBill}h billable). KPI: ${kpi}h.`
  return `Not submitted yet. Grid: ${grid}h (${gridBill}h billable). KPI: ${kpi}h. Submit from Time Tracking when totals are final.`
}

function HomeDashboard({ session }: { session: Session }) {
  const role = session.profile.role
  const isIcOnly = role === 'IC'
  const isAdmin = role === 'Admin'
  const isFinanceHub = role === 'Admin' || role === 'Finance'
  const isReviewer = role === 'Admin' || role === 'Manager' || role === 'Partner'
  const quickCreateClient = role === 'Admin' || role === 'Partner' || role === 'Finance'
  const quickCreateProject = role === 'Admin' || role === 'Partner'
  const usd = useMemo(() => new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }), [])
  const [kpis, setKpis] = useState({
    activeClients: 0,
    activeProjects: 0,
    weekHours: 0,
    loading: true,
  })
  const [approvalSnap, setApprovalSnap] = useState<{
    loading: boolean
    error: string | null
    myExpenses: ExpenseRow[]
    myPtoRequests: PtoRequestRow[]
    teamPending: ExpenseRow[]
    quotes: QuoteRow[]
    myTimesheetWeek: TimesheetWeekStatusPayload | null
    pendingTimesheetWeeks: PendingTimesheetWeek[]
  }>({
    loading: true,
    error: null,
    myExpenses: [],
    myPtoRequests: [],
    teamPending: [],
    quotes: [],
    myTimesheetWeek: null,
    pendingTimesheetWeeks: [],
  })
  const [timesheetActionBusy, setTimesheetActionBusy] = useState<string | null>(null)
  const [timesheetActionError, setTimesheetActionError] = useState<string | null>(null)

  const weekStart = useMemo(() => toYmd(weekStartMonday(new Date())), [])

  const loadDashboard = useCallback(async () => {
    setKpis((p) => ({ ...p, loading: true }))
    setApprovalSnap((p) => ({ ...p, loading: true, error: null }))
    setTimesheetActionError(null)
    try {
      const [
        clients,
        projects,
        lines,
        myExpenses,
        myPtoRequests,
        teamPending,
        quotes,
        myTimesheetWeek,
        pendingTimesheetWeeks,
      ] = await Promise.all([
        listClients(session.token, undefined, isAdmin),
        listProjects(session.token, { includeInactive: isAdmin }),
        getTimesheetWeek(session.token, weekStart),
        listMyExpenses(session.token),
        listMyPtoRequests(session.token),
        isReviewer ? listPendingExpenseApprovals(session.token) : Promise.resolve([] as ExpenseRow[]),
        isFinanceHub ? listQuotes(session.token) : Promise.resolve([] as QuoteRow[]),
        getTimesheetWeekStatus(session.token, weekStart),
        isReviewer
          ? listPendingTimesheetWeekApprovals(session.token)
          : Promise.resolve([] as PendingTimesheetWeek[]),
      ])
      const activeClients = clients.filter((c) => c.isActive).length
      const activeProjects = projects.filter((p) => p.isActive).length
      const weekHours = lines.reduce((sum, line) => sum + line.hours, 0)
      setKpis({ activeClients, activeProjects, weekHours, loading: false })
      const mineSorted = [...myExpenses].sort((a, b) => b.expenseDate.localeCompare(a.expenseDate))
      setApprovalSnap({
        loading: false,
        error: null,
        myExpenses: mineSorted.slice(0, 10),
        myPtoRequests,
        teamPending: teamPending.slice(0, 10),
        quotes: quotes.slice(0, 8),
        myTimesheetWeek,
        pendingTimesheetWeeks: pendingTimesheetWeeks.slice(0, 10),
      })
    } catch {
      setKpis((prev) => ({ ...prev, loading: false }))
      setApprovalSnap({
        loading: false,
        error: 'Could not load approval status.',
        myExpenses: [],
        myPtoRequests: [],
        teamPending: [],
        quotes: [],
        myTimesheetWeek: null,
        pendingTimesheetWeeks: [],
      })
    }
  }, [isAdmin, isFinanceHub, isReviewer, role, session.token, weekStart])

  useEffect(() => {
    void loadDashboard()
  }, [loadDashboard])

  const myPendingExpenses = useMemo(
    () => approvalSnap.myExpenses.filter((e) => e.status === 'Pending'),
    [approvalSnap.myExpenses],
  )
  const myPendingPto = useMemo(
    () => approvalSnap.myPtoRequests.filter((p) => p.status === 'Pending'),
    [approvalSnap.myPtoRequests],
  )
  const timeWeekPending =
    approvalSnap.myTimesheetWeek?.status === 'Pending' &&
    (role === 'IC' || role === 'Finance' || role === 'Manager' || role === 'Partner' || role === 'Admin')

  const pendingYourCount =
    (timeWeekPending ? 1 : 0) + myPendingExpenses.length + myPendingPto.length

  return (
    <div className="dashboard">
      <section className="card admin-card">
        <h1 className="title admin-title">Home Dashboard</h1>
        <p className="subtitle admin-sub">
          Welcome back, {session.profile.displayName} ({session.profile.role})
        </p>
        <p className="admin-hint" style={{ marginBottom: 0 }}>
          {isIcOnly
            ? 'Browse clients, projects, Resource Tracker, and reports from the header (read-only). Use Time Tracking and Expenses to log hours and submit your expenses.'
            : 'Use top navigation for full modules; quick actions and the status panel summarize common follow-ups.'}
        </p>
      </section>

      <section className="dashboard-kpis">
        <article className="card admin-card kpi-card">
          <p className="kpi-label">Active Clients</p>
          <p className="kpi-value">{kpis.loading ? '--' : kpis.activeClients}</p>
        </article>
        <article className="card admin-card kpi-card">
          <p className="kpi-label">Active Projects</p>
          <p className="kpi-value">{kpis.loading ? '--' : kpis.activeProjects}</p>
        </article>
        <article className="card admin-card kpi-card">
          <p className="kpi-label kpi-label-with-help">
            <span>Hours This Week</span>
            <span
              className="kpi-help-trigger"
              tabIndex={0}
              title={hoursThisWeekHelpTooltip(role, kpis.weekHours, approvalSnap)}
              aria-label={hoursThisWeekHelpTooltip(role, kpis.weekHours, approvalSnap)}
            >
              ⓘ
            </span>
          </p>
          <p className="kpi-value">{kpis.loading ? '--' : kpis.weekHours.toFixed(2)}</p>
          {!kpis.loading &&
          approvalSnap.myTimesheetWeek?.status === 'Pending' &&
          approvalSnap.myTimesheetWeek.pendingSubmissionTotalHours != null ? (
            <p className="kpi-sub muted">
              In approval queue (snapshot at submit):{' '}
              {approvalSnap.myTimesheetWeek.pendingSubmissionTotalHours.toFixed(2)}h total ·{' '}
              {(approvalSnap.myTimesheetWeek.pendingSubmissionBillableHours ?? 0).toFixed(2)}h billable
            </p>
          ) : null}
        </article>
      </section>

      <section className="dashboard-two-col">
        <article className="card admin-card quick-actions">
          <h2 className="admin-h2">Quick Actions</h2>
          <div className="quick-action-grid">
            <NavLink to="/timesheet" className="quick-action-tile qa-timesheet">
              <span className="quick-action-title">Time Tracking</span>
              <span className="quick-action-sub">Log hours by client, project, and task</span>
            </NavLink>
            <NavLink to="/resource-tracker" className="quick-action-tile qa-projects">
              <span className="quick-action-title">Resource Tracker</span>
              <span className="quick-action-sub">Org month view from logged hours</span>
            </NavLink>
            <NavLink to="/expenses" className="quick-action-tile qa-projects">
              <span className="quick-action-title">Track Expenses</span>
              <span className="quick-action-sub">Submit expenses for approval</span>
            </NavLink>
            {quickCreateClient ? (
              <NavLink to="/clients" className="quick-action-tile qa-clients">
                <span className="quick-action-title">Create Client</span>
                <span className="quick-action-sub">Add A Customer (Partner, Finance, Or Admin)</span>
              </NavLink>
            ) : (
              <NavLink to="/clients" className="quick-action-tile qa-clients">
                <span className="quick-action-title">Clients</span>
                <span className="quick-action-sub">
                  {isIcOnly
                    ? 'Browse the directory (read-only for IC)'
                    : 'Browse Directory; New Clients Are Created By Partner Or Finance'}
                </span>
              </NavLink>
            )}
            {quickCreateProject ? (
              <NavLink to="/projects" className="quick-action-tile qa-projects">
                <span className="quick-action-title">Create project</span>
                <span className="quick-action-sub">Start an engagement (Admin or Partner)</span>
              </NavLink>
            ) : (
              <NavLink to="/projects" className="quick-action-tile qa-projects">
                <span className="quick-action-title">Projects</span>
                <span className="quick-action-sub">
                  {isIcOnly
                    ? 'Browse engagements and budgets (read-only for IC)'
                    : 'Browse Engagements And Budget In The Directory'}
                </span>
              </NavLink>
            )}
            {isFinanceHub ? (
              <NavLink to="/finance" className="quick-action-tile qa-clients">
                <span className="quick-action-title">Finance Hub</span>
                <span className="quick-action-sub">Register, Quotes, Pipeline</span>
              </NavLink>
            ) : null}
            {isAdmin ? (
              <NavLink to="/admin/users" className="quick-action-tile qa-users">
                <span className="quick-action-title">Add User</span>
                <span className="quick-action-sub">Invite Teammate + Assign Role</span>
              </NavLink>
            ) : null}
          </div>
        </article>

        <article className="card admin-card status-snapshot-card">
          <h2 className="admin-h2">Approvals &amp; Status</h2>
          <p className="status-snapshot-hint">
            <strong>Waiting on reviewers</strong> lists only items still pending (weekly time submission, expenses, PTO).
            Time uses a snapshot at submit so the queue does not mix old totals with new grid edits. Routing: IC → project
            DM or EP, else org manager; Finance → reporting partner; Manager &amp; Partner → engagement partner; Admin
            self-signs.
          </p>
          {timesheetActionError ? (
            <p className="admin-hint" style={{ marginBottom: 8, color: 'var(--danger, #b42318)' }}>
              {timesheetActionError}
            </p>
          ) : null}
          {approvalSnap.loading ? (
            <p className="admin-hint" style={{ marginBottom: 0 }}>
              Loading…
            </p>
          ) : approvalSnap.error ? (
            <p className="admin-hint" style={{ marginBottom: 0 }}>
              {approvalSnap.error}
            </p>
          ) : (
            <div className="status-snapshot-body">
              <div className="status-block home-pending-block">
                <h3 className="status-block-title">Waiting on reviewers (your submissions)</h3>
                {pendingYourCount === 0 ? (
                  <p className="status-empty">Nothing pending — you are not waiting on anyone for time, expenses, or PTO.</p>
                ) : (
                  <ul className="status-row-list home-pending-by-type">
                    <li className="home-pending-type">
                      <span className="home-pending-type-label">Time (week)</span>
                      {timeWeekPending && approvalSnap.myTimesheetWeek ? (
                        <ul className="status-row-list">
                          <li className="home-status-row">
                            <NavLink
                              to={`/timesheet?week=${encodeURIComponent(approvalSnap.myTimesheetWeek.weekStart)}`}
                              className="home-status-label"
                            >
                              Week of {approvalSnap.myTimesheetWeek.weekStart} · submission{' '}
                              {(approvalSnap.myTimesheetWeek.pendingSubmissionTotalHours ?? approvalSnap.myTimesheetWeek.totalHours).toFixed(2)}h
                              total (
                              {(approvalSnap.myTimesheetWeek.pendingSubmissionBillableHours ?? approvalSnap.myTimesheetWeek.billableHours).toFixed(2)}
                              h billable) · grid now {approvalSnap.myTimesheetWeek.totalHours.toFixed(2)}h
                            </NavLink>
                            <StatusBadge variant="pending" />
                          </li>
                        </ul>
                      ) : (
                        <p className="status-empty home-pending-none">None</p>
                      )}
                    </li>
                    <li className="home-pending-type">
                      <span className="home-pending-type-label">Expenses</span>
                      {myPendingExpenses.length === 0 ? (
                        <p className="status-empty home-pending-none">None</p>
                      ) : (
                        <ul className="status-row-list">
                          {myPendingExpenses.map((e) => (
                            <li key={e.id} className="home-status-row">
                              <NavLink to="/expenses" className="home-status-label">
                                {e.expenseDate} · {e.client} / {e.project} · {usd.format(e.amount)}
                              </NavLink>
                              <StatusBadge variant="pending" />
                            </li>
                          ))}
                        </ul>
                      )}
                    </li>
                    <li className="home-pending-type">
                      <span className="home-pending-type-label">PTO</span>
                      {myPendingPto.length === 0 ? (
                        <p className="status-empty home-pending-none">None</p>
                      ) : (
                        <ul className="status-row-list">
                          {myPendingPto.map((p) => (
                            <li key={p.id} className="home-status-row">
                              <NavLink to="/timesheet#pto-requests" className="home-status-label">
                                {p.startDate} → {p.endDate}
                                {p.reason.trim() ? ` · ${p.reason.trim()}` : ''}
                              </NavLink>
                              <StatusBadge variant="pending" />
                            </li>
                          ))}
                        </ul>
                      )}
                    </li>
                  </ul>
                )}
              </div>

              {approvalSnap.myTimesheetWeek &&
              (role === 'IC' ||
                role === 'Finance' ||
                role === 'Manager' ||
                role === 'Partner' ||
                role === 'Admin') ? (
                <div className="status-block">
                  <h3 className="status-block-title">Your week (status)</h3>
                  <ul className="status-row-list">
                    <li className="home-status-row">
                      <NavLink
                        to={`/timesheet?week=${encodeURIComponent(approvalSnap.myTimesheetWeek.weekStart)}`}
                        className="home-status-label"
                      >
                        Week of {approvalSnap.myTimesheetWeek.weekStart} ·{' '}
                        {approvalSnap.myTimesheetWeek.totalHours.toFixed(2)}h on grid ·{' '}
                        {approvalSnap.myTimesheetWeek.billableHours.toFixed(2)}h billable
                      </NavLink>
                      <StatusBadge variant={timesheetWeekStatusVariant(approvalSnap.myTimesheetWeek.status)} />
                    </li>
                  </ul>
                </div>
              ) : null}
              <div className="status-block">
                <h3 className="status-block-title">Your expenses (recent)</h3>
                {approvalSnap.myExpenses.length === 0 ? (
                  <p className="status-empty">No expenses yet.</p>
                ) : (
                  <ul className="status-row-list">
                    {approvalSnap.myExpenses.map((e) => (
                      <li key={e.id} className="home-status-row">
                        <NavLink to="/expenses" className="home-status-label">
                          {e.expenseDate} · {e.client} / {e.project} · {usd.format(e.amount)}
                        </NavLink>
                        <StatusBadge variant={expenseUiStatus(e.status)} />
                      </li>
                    ))}
                  </ul>
                )}
              </div>
              {isReviewer ? (
                <div className="status-block">
                  <h3 className="status-block-title">Team Queue (Your Review)</h3>
                  {approvalSnap.teamPending.length === 0 ? (
                    <p className="status-empty">Nothing waiting for you.</p>
                  ) : (
                    <ul className="status-row-list">
                      {approvalSnap.teamPending.map((e) => (
                        <li key={e.id} className="home-status-row">
                          <NavLink to="/expenses#pending-expense-approvals" className="home-status-label">
                            {e.userEmail} · {e.client} / {e.project}
                          </NavLink>
                          <StatusBadge variant="pending" />
                        </li>
                      ))}
                    </ul>
                  )}
                  <h3 className="status-block-title" style={{ marginTop: '1rem' }}>
                    Time Tracking Weeks (Pending)
                  </h3>
                  {approvalSnap.pendingTimesheetWeeks.length === 0 ? (
                    <p className="status-empty">No timesheet weeks awaiting your approval.</p>
                  ) : (
                    <ul className="status-row-list">
                      {approvalSnap.pendingTimesheetWeeks.map((t) => {
                        const key = `${t.userId}:${t.weekStart}`
                        const busy = timesheetActionBusy === key
                        return (
                          <li key={key} className="home-status-row home-status-row-wrap">
                            <span className="home-status-label">
                              {t.userEmail} · Week Of {t.weekStart} · {t.billableHours.toFixed(2)}h billable
                            </span>
                            <span className="home-status-ts-actions">
                              <NavLink
                                className="btn secondary btn-sm"
                                style={{ textDecoration: 'none', display: 'inline-flex', alignItems: 'center' }}
                                to={`/timesheet/review?userId=${encodeURIComponent(t.userId)}&week=${encodeURIComponent(t.weekStart)}`}
                              >
                                Review
                              </NavLink>
                              <button
                                type="button"
                                className="btn secondary btn-sm"
                                disabled={busy}
                                onClick={async () => {
                                  setTimesheetActionError(null)
                                  setTimesheetActionBusy(key)
                                  try {
                                    await approveTimesheetWeek(session.token, t.userId, t.weekStart)
                                    await loadDashboard()
                                  } catch (err) {
                                    setTimesheetActionError(err instanceof Error ? err.message : 'Approve failed')
                                  } finally {
                                    setTimesheetActionBusy(null)
                                  }
                                }}
                              >
                                Approve
                              </button>
                              <button
                                type="button"
                                className="btn secondary btn-sm"
                                disabled={busy}
                                onClick={async () => {
                                  if (!window.confirm(`Reject timesheet for ${t.userEmail} (${t.weekStart})?`)) return
                                  setTimesheetActionError(null)
                                  setTimesheetActionBusy(key)
                                  try {
                                    await rejectTimesheetWeek(session.token, t.userId, t.weekStart)
                                    await loadDashboard()
                                  } catch (err) {
                                    setTimesheetActionError(err instanceof Error ? err.message : 'Reject failed')
                                  } finally {
                                    setTimesheetActionBusy(null)
                                  }
                                }}
                              >
                                Reject
                              </button>
                            </span>
                          </li>
                        )
                      })}
                    </ul>
                  )}
                </div>
              ) : null}
              {isFinanceHub ? (
                <div className="status-block">
                  <h3 className="status-block-title">Quotes</h3>
                  {approvalSnap.quotes.length === 0 ? (
                    <p className="status-empty">No quotes yet.</p>
                  ) : (
                    <ul className="status-row-list">
                      {approvalSnap.quotes.map((q) => (
                        <li key={q.id} className="home-status-row">
                          <NavLink to="/finance" className="home-status-label">
                            Quote · {q.clientName} · {q.title}
                          </NavLink>
                          <StatusBadge variant={quoteUiStatus(q.status)} />
                        </li>
                      ))}
                    </ul>
                  )}
                </div>
              ) : null}
            </div>
          )}
        </article>
      </section>

      <section className="card admin-card">
        <h2 className="admin-h2">Current Delivery Scope</h2>
        <ul className="dashboard-list">
          <li>Authentication and role-based access controls</li>
          <li>
            Time Tracking weekly entry and org Resource Tracker; IC browses clients, projects, Resource Tracker, and personal
            reports (read-only); Partner and Finance create clients; Admin and Partner create projects and staffing;
            Finance updates budget on assigned projects; managers see full per-line expense detail on projects where IC
            sees rollups only
          </li>
          <li>Client directory{isIcOnly ? ' — IC browse only' : ''}</li>
          <li>
            Project directory (filters; catalog edits Admin or Partner; per-project detail for rollups and finance budget)
          </li>
          <li>Personal reports (time + expense totals by month)</li>
          {isAdmin ? <li>User administration and role assignment</li> : null}
          {isFinanceHub ? <li>Finance register and client quoting</li> : null}
        </ul>
      </section>
    </div>
  )
}

function LoginPage({ onSignedIn }: { onSignedIn: (s: Session) => void }) {
  const nav = useNavigate()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const onSubmit = useCallback(
    async (e: React.FormEvent) => {
      e.preventDefault()
      setError(null)
      setBusy(true)
      try {
        const tok = await login(email.trim(), password)
        const profile = await me(tok.accessToken)
        onSignedIn({ token: tok.accessToken, profile })
        nav('/', { replace: true })
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Sign-in failed')
      } finally {
        setBusy(false)
      }
    },
    [email, password, nav, onSignedIn],
  )

  return (
    <main className="shell">
      <div className="card login-card">
        <img className="login-logo" src="/g2e-logo.png" alt="G2E logo" />
        <h1 className="title">Log in to your account</h1>
        <p className="subtitle">Sign in with your work email</p>
        <form
          className="form"
          onSubmit={onSubmit}
          onKeyDown={(e) => {
            if (e.key !== 'Enter' || busy) return
            if ((e.target as HTMLElement).tagName !== 'INPUT') return
            e.preventDefault()
            ;(e.currentTarget as HTMLFormElement).requestSubmit()
          }}
        >
          <label className="field">
            <span>Email</span>
            <input type="email" autoComplete="username" value={email} onChange={(e) => setEmail(e.target.value)} required />
          </label>
          <label className="field">
            <span>Password</span>
            <input
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
            />
          </label>
          {error ? (
            <p className="err" role="alert">
              {error}
            </p>
          ) : null}
          <button type="submit" className="btn primary" disabled={busy}>
            {busy ? 'Signing In...' : 'Sign In'}
          </button>
        </form>
      </div>
    </main>
  )
}

function AuthenticatedLayout({
  session,
  onSignOut,
}: {
  session: Session | null
  onSignOut: () => void
}) {
  const location = useLocation()
  const [density, setDensity] = useState<'comfortable' | 'compact'>(() => {
    return localStorage.getItem('c2e-density') === 'compact' ? 'compact' : 'comfortable'
  })

  const toggleDensity = useCallback(() => {
    setDensity((prev) => {
      const next = prev === 'comfortable' ? 'compact' : 'comfortable'
      localStorage.setItem('c2e-density', next)
      return next
    })
  }, [])

  if (!session) return <Navigate to="/login" replace />

  const isAdmin = session.profile.role === 'Admin'
  const isFinanceHub = session.profile.role === 'Admin' || session.profile.role === 'Finance'

  return (
    <div className={`app-shell density-${density}`}>
      <header className="topbar">
        <div className="topbar-brand">
          <img className="topbar-brand-img" src="/g2e-wordmark.png" alt="G2E" />
        </div>
        <nav className="topbar-tabs" aria-label="Primary navigation">
          <NavLink to="/" end className={({ isActive }) => `topbar-tab${isActive ? ' active' : ''}`}>
            Home
          </NavLink>
          <NavLink to="/timesheet" className={({ isActive }) => `topbar-tab${isActive ? ' active' : ''}`}>
            Time Tracking
          </NavLink>
          <NavLink
            to="/resource-tracker"
            className={() =>
              `topbar-tab${location.pathname.startsWith('/resource-tracker') ? ' active' : ''}`
            }
          >
            Resource Tracker
          </NavLink>
          <NavLink to="/expenses" className={({ isActive }) => `topbar-tab${isActive ? ' active' : ''}`}>
            Expenses
          </NavLink>
          <NavLink to="/clients" className={({ isActive }) => `topbar-tab${isActive ? ' active' : ''}`}>
            Clients
          </NavLink>
          <NavLink to="/projects" className={({ isActive }) => `topbar-tab${isActive ? ' active' : ''}`}>
            Projects
          </NavLink>
          <NavLink to="/reports" className={({ isActive }) => `topbar-tab${isActive ? ' active' : ''}`}>
            Reports
          </NavLink>
          {isFinanceHub ? (
            <NavLink to="/finance" className={({ isActive }) => `topbar-tab${isActive ? ' active' : ''}`}>
              Finance
            </NavLink>
          ) : null}
          {isAdmin ? (
            <NavLink to="/admin/users" className={({ isActive }) => `topbar-tab${isActive ? ' active' : ''}`}>
              User Management
            </NavLink>
          ) : null}
        </nav>
        <div className="topbar-user">
          <button type="button" className="btn secondary btn-sm" onClick={toggleDensity}>
            {density === 'comfortable' ? 'Compact View' : 'Comfortable View'}
          </button>
          <span>{session.profile.displayName}</span>
          <button type="button" className="btn secondary btn-sm" onClick={onSignOut}>
            Sign Out
          </button>
        </div>
      </header>
      <main className="page-content">
        <Outlet />
      </main>
    </div>
  )
}

function AdminUsersRoute({ session, onSignOut }: { session: Session | null; onSignOut: () => void }) {
  if (!session) return <Navigate to="/login" replace />
  if (session.profile.role !== 'Admin') return <Navigate to="/" replace />
  return <AdminUsers token={session.token} profile={session.profile} onSignOut={onSignOut} />
}

function TimesheetRoute({ session }: { session: Session | null }) {
  if (!session) return <Navigate to="/login" replace />
  return <TimesheetWeek token={session.token} profile={session.profile} />
}

function TimesheetApprovalReviewRoute({ session }: { session: Session | null }) {
  if (!session) return <Navigate to="/login" replace />
  if (
    session.profile.role !== 'Admin' &&
    session.profile.role !== 'Manager' &&
    session.profile.role !== 'Partner'
  ) {
    return <Navigate to="/" replace />
  }
  return <TimesheetApprovalReview token={session.token} profile={session.profile} />
}

function ClientsRoute({ session }: { session: Session | null }) {
  if (!session) return <Navigate to="/login" replace />
  return <ClientsPage token={session.token} profile={session.profile} />
}

function ExpensesRoute({ session }: { session: Session | null }) {
  if (!session) return <Navigate to="/login" replace />
  return <ExpensesPage token={session.token} profile={session.profile} />
}

function ProjectsRoute({ session }: { session: Session | null }) {
  if (!session) return <Navigate to="/login" replace />
  return <ProjectsPage token={session.token} profile={session.profile} />
}

function ProjectDetailRoute({ session }: { session: Session | null }) {
  if (!session) return <Navigate to="/login" replace />
  return <ProjectDetailPage token={session.token} profile={session.profile} />
}

function ReportsRoute({ session }: { session: Session | null }) {
  if (!session) return <Navigate to="/login" replace />
  return <ReportsPage token={session.token} profile={session.profile} />
}

function FinanceRoute({ session }: { session: Session | null }) {
  if (!session) return <Navigate to="/login" replace />
  if (session.profile.role !== 'Admin' && session.profile.role !== 'Finance')
    return <Navigate to="/" replace />
  return <FinancePage token={session.token} profile={session.profile} />
}

function AppRoutes() {
  const [session, setSession] = useState<Session | null>(null)
  const signOut = useCallback(() => setSession(null), [])
  return (
    <Routes>
      <Route path="/login" element={<LoginPage onSignedIn={setSession} />} />
      <Route element={<AuthenticatedLayout session={session} onSignOut={signOut} />}>
        <Route path="/" element={session ? <HomeDashboard session={session} /> : <Navigate to="/login" replace />} />
        <Route path="/admin/users" element={<AdminUsersRoute session={session} onSignOut={signOut} />} />
        <Route path="/timesheet/review" element={<TimesheetApprovalReviewRoute session={session} />} />
        <Route path="/timesheet" element={<TimesheetRoute session={session} />} />
        <Route
          path="/resource-tracker"
          element={
            session ? <ResourceTrackerLayout session={session} /> : <Navigate to="/login" replace />
          }
        >
          <Route index element={<ResourceTracker />} />
          <Route path="project-tasks" element={<ResourceTrackerProjectTasks />} />
        </Route>
        <Route path="/expenses" element={<ExpensesRoute session={session} />} />
        <Route path="/clients" element={<ClientsRoute session={session} />} />
        <Route path="/projects" element={<ProjectsRoute session={session} />} />
        <Route path="/projects/:projectId" element={<ProjectDetailRoute session={session} />} />
        <Route path="/reports" element={<ReportsRoute session={session} />} />
        <Route path="/finance" element={<FinanceRoute session={session} />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}

export default function App() {
  return (
    <BrowserRouter>
      <AppRoutes />
    </BrowserRouter>
  )
}
