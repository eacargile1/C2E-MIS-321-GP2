import { useCallback, useEffect, useMemo, useState } from 'react'
import { BrowserRouter, NavLink, Navigate, Outlet, Route, Routes, useNavigate } from 'react-router-dom'
import {
  approveTimesheetWeek,
  getTimesheetWeek,
  getTimesheetWeekStatus,
  listClients,
  listMyExpenses,
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
  if (!tw) return 'Timesheet status not available.'
  const h = weekHours.toFixed(2)
  const bill = tw.billableHours.toFixed(2)

  const usesWeekSignoff =
    role === 'IC' ||
    role === 'Finance' ||
    role === 'Manager' ||
    role === 'Partner' ||
    role === 'Admin'

  if (!usesWeekSignoff) {
    return `You have ${h}h logged this week (${bill}h billable).`
  }

  if (role === 'Admin') {
    if (tw.status === 'Approved')
      return `Admin weeks self-sign on submit (${bill}h billable in the signed week). You have ${h}h logged this calendar week on the grid.`
    return `You have ${h}h logged this week (${bill}h billable). Submit from Timesheet when you want this week recorded as signed-off.`
  }

  if (tw.status === 'Pending')
    return `All ${h}h you logged this week are pending approval (${bill}h billable in that submission).`
  if (tw.status === 'Approved')
    return `No hours pending approval — this week is approved (${bill}h billable were in the last submission).`
  if (tw.status === 'Rejected')
    return `No hours are in the approval queue right now (week was rejected). You still have ${h}h on the timesheet — edit and resubmit when ready.`
  return `This week is not submitted yet (${h}h logged, ${bill}h billable on lines). Submit from Timesheet when totals are final.`
}

function HomeDashboard({ session }: { session: Session }) {
  const role = session.profile.role
  const isIcOnly = role === 'IC'
  const isAdmin = role === 'Admin'
  const isFinanceHub = role === 'Admin' || role === 'Finance'
  const isReviewer = role === 'Admin' || role === 'Manager' || role === 'Partner'
  const quickCreateClient = role === 'Admin' || role === 'Partner' || role === 'Finance'
  const quickCreateProject = role === 'Admin' || role === 'Partner'
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
    teamPending: ExpenseRow[]
    quotes: QuoteRow[]
    myTimesheetWeek: TimesheetWeekStatusPayload | null
    pendingTimesheetWeeks: PendingTimesheetWeek[]
  }>({
    loading: true,
    error: null,
    myExpenses: [],
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
        teamPending,
        quotes,
        myTimesheetWeek,
        pendingTimesheetWeeks,
      ] = await Promise.all([
        isIcOnly
          ? Promise.resolve([] as Awaited<ReturnType<typeof listClients>>)
          : listClients(session.token, undefined, isAdmin),
        isIcOnly
          ? Promise.resolve([] as Awaited<ReturnType<typeof listProjects>>)
          : listProjects(session.token, { includeInactive: isAdmin }),
        getTimesheetWeek(session.token, weekStart),
        listMyExpenses(session.token),
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
        teamPending: [],
        quotes: [],
        myTimesheetWeek: null,
        pendingTimesheetWeeks: [],
      })
    }
  }, [isAdmin, isFinanceHub, isIcOnly, isReviewer, role, session.token, weekStart])

  useEffect(() => {
    void loadDashboard()
  }, [loadDashboard])

  return (
    <div className="dashboard">
      <section className="card admin-card">
        <h1 className="title admin-title">Home Dashboard</h1>
        <p className="subtitle admin-sub">
          Welcome back, {session.profile.displayName} ({session.profile.role})
        </p>
        <p className="admin-hint" style={{ marginBottom: 0 }}>
          {isIcOnly
            ? 'Use Timesheet and Expenses in the header to log hours and submit expenses.'
            : 'Use top navigation for full modules; quick actions and the status panel summarize common follow-ups.'}
        </p>
      </section>

      <section className="dashboard-kpis">
        {isIcOnly ? null : (
          <>
            <article className="card admin-card kpi-card">
              <p className="kpi-label">Active Clients</p>
              <p className="kpi-value">{kpis.loading ? '--' : kpis.activeClients}</p>
            </article>
            <article className="card admin-card kpi-card">
              <p className="kpi-label">Active Projects</p>
              <p className="kpi-value">{kpis.loading ? '--' : kpis.activeProjects}</p>
            </article>
          </>
        )}
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
        </article>
      </section>

      <section className="dashboard-two-col">
        <article className="card admin-card quick-actions">
          <h2 className="admin-h2">Quick Actions</h2>
          <div className="quick-action-grid">
            <NavLink to="/timesheet" className="quick-action-tile qa-timesheet">
              <span className="quick-action-title">Timesheet</span>
              <span className="quick-action-sub">Log Hours By Client, Project, And Task</span>
            </NavLink>
            {isIcOnly ? null : (
              <NavLink to="/resource-tracker" className="quick-action-tile qa-projects">
                <span className="quick-action-title">Resource tracker</span>
                <span className="quick-action-sub">Org month view from logged hours</span>
              </NavLink>
            )}
            <NavLink to="/expenses" className="quick-action-tile qa-projects">
              <span className="quick-action-title">Track Expense</span>
              <span className="quick-action-sub">Submit Expenses For Approval</span>
            </NavLink>
            {isIcOnly ? null : quickCreateClient ? (
              <NavLink to="/clients" className="quick-action-tile qa-clients">
                <span className="quick-action-title">Create Client</span>
                <span className="quick-action-sub">Add A Customer (Partner, Finance, Or Admin)</span>
              </NavLink>
            ) : (
              <NavLink to="/clients" className="quick-action-tile qa-clients">
                <span className="quick-action-title">Clients</span>
                <span className="quick-action-sub">Browse Directory; New Clients Are Created By Partner Or Finance</span>
              </NavLink>
            )}
            {isIcOnly ? null : quickCreateProject ? (
              <NavLink to="/projects" className="quick-action-tile qa-projects">
                <span className="quick-action-title">Create project</span>
                <span className="quick-action-sub">Start an engagement (Admin or Partner)</span>
              </NavLink>
            ) : (
              <NavLink to="/projects" className="quick-action-tile qa-projects">
                <span className="quick-action-title">Projects</span>
                <span className="quick-action-sub">Browse Engagements And Budget In The Directory</span>
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
            Expenses and weekly timesheet sign-off (IC & Finance → delivery manager path; Manager & Partner → engagement
            partner; Admin self-signs). Open Timesheet to edit or submit.
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
              {approvalSnap.myTimesheetWeek &&
              (role === 'IC' ||
                role === 'Finance' ||
                role === 'Manager' ||
                role === 'Partner' ||
                role === 'Admin') ? (
                <div className="status-block">
                  <h3 className="status-block-title">Your Timesheet (This Week)</h3>
                  <ul className="status-row-list">
                    <li className="home-status-row">
                      <NavLink
                        to={`/timesheet?week=${encodeURIComponent(approvalSnap.myTimesheetWeek.weekStart)}`}
                        className="home-status-label"
                      >
                        Week of {approvalSnap.myTimesheetWeek.weekStart} ·{' '}
                        {approvalSnap.myTimesheetWeek.totalHours.toFixed(2)}h total ·{' '}
                        {approvalSnap.myTimesheetWeek.billableHours.toFixed(2)}h billable
                      </NavLink>
                      <StatusBadge variant={timesheetWeekStatusVariant(approvalSnap.myTimesheetWeek.status)} />
                    </li>
                  </ul>
                </div>
              ) : null}
              <div className="status-block">
                <h3 className="status-block-title">Your Expenses</h3>
                {approvalSnap.myExpenses.length === 0 ? (
                  <p className="status-empty">No expenses yet.</p>
                ) : (
                  <ul className="status-row-list">
                    {approvalSnap.myExpenses.map((e) => (
                      <li key={e.id} className="home-status-row">
                        <NavLink to="/expenses" className="home-status-label">
                          Expense · {e.client} / {e.project}
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
                          <NavLink to="/expenses" className="home-status-label">
                            {e.userEmail} · {e.client} / {e.project}
                          </NavLink>
                          <StatusBadge variant="pending" />
                        </li>
                      ))}
                    </ul>
                  )}
                  <h3 className="status-block-title" style={{ marginTop: '1rem' }}>
                    Timesheet Weeks (Pending)
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
        {isIcOnly ? (
          <ul className="dashboard-list">
            <li>Your weekly timesheet (this account type cannot view org-wide resource or directory modules)</li>
            <li>Expense submission and tracking your own reimbursement requests</li>
          </ul>
        ) : (
          <ul className="dashboard-list">
            <li>Authentication and role-based access controls</li>
            <li>
              Timesheet weekly entry and org resource tracker (split views); IC browses clients and projects; Partner and
              Finance create clients; Admin and Partner create projects and staffing; Finance updates budget on assigned
              projects; managers see project and expense rollups without editing the catalog
            </li>
            <li>Client management directory</li>
            <li>
              Project directory (filters; catalog edits Admin or Partner; per-project detail for rollups and finance budget)
            </li>
            <li>Personal reports (time + expense totals by month)</li>
            {isAdmin ? <li>User administration and role assignment</li> : null}
            {isFinanceHub ? <li>Finance register and client quoting</li> : null}
          </ul>
        )}
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
  if (!session) return <Navigate to="/login" replace />
  const isIcOnly = session.profile.role === 'IC'
  const isAdmin = session.profile.role === 'Admin'
  const isFinanceHub = session.profile.role === 'Admin' || session.profile.role === 'Finance'
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
          <NavLink to="/timesheet" end className={({ isActive }) => `topbar-tab${isActive ? ' active' : ''}`}>
            Timesheet
          </NavLink>
          {isIcOnly ? null : (
            <NavLink to="/resource-tracker" className={({ isActive }) => `topbar-tab${isActive ? ' active' : ''}`}>
              Resource tracker
            </NavLink>
          )}
          <NavLink to="/expenses" className={({ isActive }) => `topbar-tab${isActive ? ' active' : ''}`}>
            Expenses
          </NavLink>
          {isIcOnly ? null : (
            <NavLink to="/clients" className={({ isActive }) => `topbar-tab${isActive ? ' active' : ''}`}>
              Clients
            </NavLink>
          )}
          {isIcOnly ? null : (
            <NavLink to="/projects" className={({ isActive }) => `topbar-tab${isActive ? ' active' : ''}`}>
              Projects
            </NavLink>
          )}
          {isIcOnly ? null : (
            <NavLink to="/reports" className={({ isActive }) => `topbar-tab${isActive ? ' active' : ''}`}>
              Reports
            </NavLink>
          )}
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

function ResourceTrackerRoute({ session }: { session: Session | null }) {
  if (!session) return <Navigate to="/login" replace />
  if (session.profile.role === 'IC') return <Navigate to="/" replace />
  return <ResourceTracker token={session.token} profile={session.profile} />
}

function ClientsRoute({ session }: { session: Session | null }) {
  if (!session) return <Navigate to="/login" replace />
  if (session.profile.role === 'IC') return <Navigate to="/" replace />
  return <ClientsPage token={session.token} profile={session.profile} />
}

function ExpensesRoute({ session }: { session: Session | null }) {
  if (!session) return <Navigate to="/login" replace />
  return <ExpensesPage token={session.token} profile={session.profile} />
}

function ProjectsRoute({ session }: { session: Session | null }) {
  if (!session) return <Navigate to="/login" replace />
  if (session.profile.role === 'IC') return <Navigate to="/" replace />
  return <ProjectsPage token={session.token} profile={session.profile} />
}

function ProjectDetailRoute({ session }: { session: Session | null }) {
  if (!session) return <Navigate to="/login" replace />
  return <ProjectDetailPage token={session.token} profile={session.profile} />
}

function ReportsRoute({ session }: { session: Session | null }) {
  if (!session) return <Navigate to="/login" replace />
  if (session.profile.role === 'IC') return <Navigate to="/" replace />
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
        <Route path="/resource-tracker" element={<ResourceTrackerRoute session={session} />} />
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
