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
  getTimesheetWeekStatus,
  getTimesheetWeek,
  listClients,
  listMyExpenses,
  listMyPtoRequests,
  listPendingExpenseApprovals,
  listPendingTimesheetWeekApprovals,
  listProjects,
  listQuotes,
  login,
  me,
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

function HomeDashboard({ session }: { session: Session }) {
  const role = session.profile.role
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
    pendingExpenses: 0,
    loading: true,
    error: null as string | null,
  })
  const [homeSnap, setHomeSnap] = useState<{
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

  const weekStart = useMemo(() => toYmd(weekStartMonday(new Date())), [])

  const loadDashboard = useCallback(async () => {
    setKpis((p) => ({ ...p, loading: true, error: null }))
    setHomeSnap((p) => ({ ...p, loading: true, error: null }))
    try {
      const [clients, projects, lines, myExpenses, myPtoRequests, teamPending, quotes, myTimesheetWeek, pendingTimesheetWeeks] =
        await Promise.all([
        listClients(session.token, undefined, isAdmin),
        listProjects(session.token, { includeInactive: isAdmin }),
        getTimesheetWeek(session.token, weekStart),
        listMyExpenses(session.token),
        listMyPtoRequests(session.token),
        isReviewer ? listPendingExpenseApprovals(session.token) : Promise.resolve([] as ExpenseRow[]),
        isFinanceHub ? listQuotes(session.token) : Promise.resolve([] as QuoteRow[]),
        getTimesheetWeekStatus(session.token, weekStart),
        isReviewer ? listPendingTimesheetWeekApprovals(session.token) : Promise.resolve([] as PendingTimesheetWeek[]),
        ])
      const activeClients = clients.filter((c) => c.isActive).length
      const activeProjects = projects.filter((p) => p.isActive).length
      const weekHours = lines.reduce((sum, line) => sum + line.hours, 0)
      const pendingExpenses = myExpenses.filter((e) => e.status === 'Pending').length
      setKpis({ activeClients, activeProjects, weekHours, pendingExpenses, loading: false, error: null })
      const mineSorted = [...myExpenses].sort((a, b) => b.expenseDate.localeCompare(a.expenseDate))
      setHomeSnap({
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
      setKpis((prev) => ({ ...prev, loading: false, error: 'Could not load dashboard data.' }))
      setHomeSnap({
        loading: false,
        error: 'Could not load approval and activity data.',
        myExpenses: [],
        myPtoRequests: [],
        teamPending: [],
        quotes: [],
        myTimesheetWeek: null,
        pendingTimesheetWeeks: [],
      })
    }
  }, [isAdmin, isFinanceHub, isReviewer, session.token, weekStart])

  useEffect(() => {
    void loadDashboard()
  }, [loadDashboard])

  const firstName = (session.profile.displayName || session.profile.email).split(' ')[0] || 'there'
  const greeting = (() => {
    const h = new Date().getHours()
    if (h < 12) return 'Good morning'
    if (h < 18) return 'Good afternoon'
    return 'Good evening'
  })()
  const myPendingExpenses = useMemo(() => homeSnap.myExpenses.filter((e) => e.status === 'Pending'), [homeSnap.myExpenses])
  const myPendingPto = useMemo(() => homeSnap.myPtoRequests.filter((p) => p.status === 'Pending'), [homeSnap.myPtoRequests])
  const timeWeekPending = homeSnap.myTimesheetWeek?.status === 'Pending'
  const pendingYourCount = (timeWeekPending ? 1 : 0) + myPendingExpenses.length + myPendingPto.length
  const pendingForYouCount = homeSnap.teamPending.length + homeSnap.pendingTimesheetWeeks.length
  const recentActivity = useMemo(() => {
    const expenseRows = homeSnap.myExpenses.slice(0, 3).map((e) => ({
      id: `exp-${e.id}`,
      title: `${e.client} - ${e.project}`,
      meta: `${usd.format(e.amount)} · ${e.status.toLowerCase()}`,
      tag: 'Expense',
      tone: 'orange',
    }))
    const quoteRows = homeSnap.quotes.slice(0, 2).map((q) => ({
      id: `quote-${q.id}`,
      title: `${q.clientName} - ${q.title}`,
      meta: `Quote · ${q.status}`,
      tag: 'Project',
      tone: 'purple',
    }))
    const weekRow = homeSnap.myTimesheetWeek
      ? [
          {
            id: `week-${homeSnap.myTimesheetWeek.weekStart}`,
            title: 'Weekly timesheet',
            meta: `${homeSnap.myTimesheetWeek.status} · week of ${homeSnap.myTimesheetWeek.weekStart}`,
            tag: 'Time',
            tone: 'teal',
          },
        ]
      : []
    return [...weekRow, ...expenseRows, ...quoteRows].slice(0, 5)
  }, [homeSnap.myExpenses, homeSnap.myTimesheetWeek, homeSnap.quotes, usd])

  return (
    <div className="home-page">
      <section className="home-hero">
        <p className="home-eyebrow">Home</p>
        <h1 className="home-greeting">
          {greeting}, {firstName}.
        </h1>
        <p className="home-subtitle">Here&apos;s where things stand this week.</p>
      </section>

      <section className="home-kpi-grid">
        <article className="card admin-card home-kpi-card home-kpi-clients">
          <p className="home-kpi-label">Active Clients</p>
          <p className="home-kpi-value">{kpis.loading ? '--' : kpis.activeClients}</p>
          <p className="home-kpi-sub">
            {kpis.loading ? 'Loading…' : `Across ${kpis.activeProjects} active project${kpis.activeProjects === 1 ? '' : 's'}`}
          </p>
        </article>
        <article className="card admin-card home-kpi-card home-kpi-hours">
          <p className="home-kpi-label">Hours This Week</p>
          <p className="home-kpi-value">{kpis.loading ? '--' : kpis.weekHours.toFixed(2)}</p>
          <p className="home-kpi-sub">{kpis.loading ? 'Loading…' : 'No entries yet · due Friday'}</p>
        </article>
        <article className="card admin-card home-kpi-card home-kpi-expenses">
          <p className="home-kpi-label">Pending Expenses</p>
          <p className="home-kpi-value">{kpis.loading ? '--' : kpis.pendingExpenses}</p>
          <p className="home-kpi-sub">
            {kpis.loading ? 'Loading…' : kpis.pendingExpenses > 0 ? 'Awaiting your submission' : 'No pending submissions'}
          </p>
        </article>
      </section>

      {kpis.error ? <p className="home-load-error">{kpis.error}</p> : null}

      <section className="home-main-grid">
        <article className="card admin-card home-quick-card">
          <div className="home-section-head">
            <h2 className="home-section-title">Quick Actions</h2>
          </div>
          <div className="home-actions-grid">
            <NavLink to="/timesheet" className="home-action-cell action-a">
              <span className="home-action-arrow" aria-hidden />
              <span className="home-action-name">Time Tracking</span>
              <span className="home-action-desc">Log hours by client, project, and task</span>
            </NavLink>
            <NavLink to="/resource-tracker" className="home-action-cell action-b">
              <span className="home-action-arrow" aria-hidden />
              <span className="home-action-name">Resource Tracker</span>
              <span className="home-action-desc">Org month view from logged hours</span>
            </NavLink>
            <NavLink to="/expenses" className="home-action-cell action-c">
              <span className="home-action-arrow" aria-hidden />
              <span className="home-action-name">Track Expenses</span>
              <span className="home-action-desc">Submit expenses for approval</span>
            </NavLink>
            <NavLink to="/clients" className="home-action-cell action-d">
              <span className="home-action-arrow" aria-hidden />
              <span className="home-action-name">{quickCreateClient ? 'Create Client' : 'Clients'}</span>
              <span className="home-action-desc">
                {quickCreateClient ? 'Add a customer, partner, or finance contact' : 'Browse client directory'}
              </span>
            </NavLink>
            <NavLink to="/projects" className="home-action-cell action-e">
              <span className="home-action-arrow" aria-hidden />
              <span className="home-action-name">{quickCreateProject ? 'Create Project' : 'Projects'}</span>
              <span className="home-action-desc">
                {quickCreateProject ? 'Set up a new billable project' : 'Browse engagements and budgets'}
              </span>
            </NavLink>
            <NavLink to={isFinanceHub ? '/finance' : isAdmin ? '/admin/users' : '/reports'} className="home-action-cell action-f">
              <span className="home-action-arrow" aria-hidden />
              <span className="home-action-name">{isFinanceHub ? 'Finance Hub' : isAdmin ? 'User Management' : 'Reports'}</span>
              <span className="home-action-desc">
                {isFinanceHub
                  ? 'Invoices, billing, and revenue reports'
                  : isAdmin
                    ? 'Invite teammates and assign roles'
                    : 'View your time and expense trends'}
              </span>
            </NavLink>
          </div>
        </article>

        <div className="home-right-stack">
          <article className="card admin-card home-right-card">
            <div className="home-section-head">
              <h2 className="home-section-title">Pending &amp; Approvals</h2>
            </div>
            {homeSnap.loading ? (
              <p className="home-panel-empty">Loading…</p>
            ) : homeSnap.error ? (
              <p className="home-panel-empty">{homeSnap.error}</p>
            ) : (
              <div className="home-pending-body">
                <p className="home-pending-line">Your submissions pending: {pendingYourCount}</p>
                <p className="home-pending-line">Waiting for your review: {pendingForYouCount}</p>
                {timeWeekPending && homeSnap.myTimesheetWeek ? (
                  <NavLink className="home-mini-link" to={`/timesheet?week=${encodeURIComponent(homeSnap.myTimesheetWeek.weekStart)}`}>
                    Timesheet week {homeSnap.myTimesheetWeek.weekStart} is pending
                  </NavLink>
                ) : null}
                {myPendingExpenses.slice(0, 2).map((e) => (
                  <NavLink key={e.id} className="home-mini-link" to="/expenses">
                    {e.client} / {e.project} · {usd.format(e.amount)} pending
                  </NavLink>
                ))}
                {isReviewer &&
                  homeSnap.pendingTimesheetWeeks.slice(0, 2).map((w) => (
                    <NavLink
                      key={`${w.userId}:${w.weekStart}`}
                      className="home-mini-link"
                      to={`/timesheet/review?userId=${encodeURIComponent(w.userId)}&week=${encodeURIComponent(w.weekStart)}`}
                    >
                      Review {w.userEmail} · {w.weekStart}
                    </NavLink>
                  ))}
              </div>
            )}
          </article>

          <article className="card admin-card home-right-card">
            <div className="home-section-head">
              <h2 className="home-section-title">Recent Activity</h2>
            </div>
            {homeSnap.loading ? (
              <p className="home-panel-empty">Loading…</p>
            ) : recentActivity.length === 0 ? (
              <p className="home-panel-empty">No recent activity yet.</p>
            ) : (
              <ul className="home-activity-list">
                {recentActivity.map((item) => (
                  <li key={item.id} className="home-activity-row">
                    <span className={`home-status-dot ${item.tone}`} />
                    <div className="home-activity-copy">
                      <p className="home-activity-title">{item.title}</p>
                      <p className="home-activity-meta">{item.meta}</p>
                    </div>
                    <span className={`home-status-tag ${item.tone}`}>{item.tag}</span>
                  </li>
                ))}
              </ul>
            )}
          </article>
        </div>
      </section>
    </div>
  )
}

function LoginPage({ onSignedIn }: { onSignedIn: (s: Session) => void }) {
  const nav = useNavigate()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [rememberMe, setRememberMe] = useState(false)
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
      <section className="login-layout" aria-label="Employee portal sign in">
        <aside className="login-brand-panel">
          <img className="login-logo" src="/g2e-logo.png" alt="G2E logo" />
          <p className="login-brand-copy">Track your time.</p>
          <p className="login-brand-copy">Submit expenses.</p>
          <p className="login-brand-copy">Stay on budget.</p>
          <p className="login-brand-sub">The G2E employee portal for client time logging and expense reporting.</p>
        </aside>
        <div className="login-form-panel">
          <div className="login-form-inner">
            <p className="login-kicker">Employee Portal</p>
            <h1 className="title login-title">Sign in</h1>
            <form
              className="form login-form"
              onSubmit={onSubmit}
              onKeyDown={(e) => {
                if (e.key !== 'Enter' || busy) return
                if ((e.target as HTMLElement).tagName !== 'INPUT') return
                e.preventDefault()
                ;(e.currentTarget as HTMLFormElement).requestSubmit()
              }}
            >
              <label className="field login-field">
                <span>Work Email</span>
                <input
                  type="email"
                  placeholder="j.smith@g2e.com"
                  autoComplete="username"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  required
                />
              </label>
              <label className="field login-field">
                <span>Password</span>
                <input
                  type="password"
                  autoComplete="current-password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  required
                />
              </label>
              <label className="login-remember">
                <input type="checkbox" checked={rememberMe} onChange={(e) => setRememberMe(e.target.checked)} />
                <span>Keep me signed in</span>
              </label>
              {error ? (
                <p className="err" role="alert">
                  {error}
                </p>
              ) : null}
              <button type="submit" className="btn primary login-submit" disabled={busy}>
                {busy ? 'Signing In...' : 'Sign In'}
              </button>
            </form>
          </div>
        </div>
      </section>
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
