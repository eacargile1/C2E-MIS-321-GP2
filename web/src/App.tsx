import { useCallback, useEffect, useMemo, useState } from 'react'
import { BrowserRouter, NavLink, Navigate, Outlet, Route, Routes, useNavigate } from 'react-router-dom'
import { getTimesheetWeek, listClients, listProjects, login, me, type MeProfile } from './api'
import AdminUsers from './pages/AdminUsers'
import ClientsPage from './pages/Clients'
import ExpensesPage from './pages/Expenses'
import ProjectsPage from './pages/Projects'
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
  const isAdmin = session.profile.role === 'Admin'
  const [kpis, setKpis] = useState({
    activeClients: 0,
    activeProjects: 0,
    weekHours: 0,
    loading: true,
  })

  const weekStart = useMemo(() => toYmd(weekStartMonday(new Date())), [])

  useEffect(() => {
    let cancelled = false
    async function loadKpis() {
      try {
        const [clients, projects, lines] = await Promise.all([
          listClients(session.token, undefined, isAdmin),
          listProjects(session.token, { includeInactive: isAdmin }),
          getTimesheetWeek(session.token, weekStart),
        ])
        if (cancelled) return
        const activeClients = clients.filter((c) => c.isActive).length
        const activeProjects = projects.filter((p) => p.isActive).length
        const weekHours = lines.reduce((sum, line) => sum + line.hours, 0)
        setKpis({ activeClients, activeProjects, weekHours, loading: false })
      } catch {
        if (cancelled) return
        setKpis((prev) => ({ ...prev, loading: false }))
      }
    }
    void loadKpis()
    return () => {
      cancelled = true
    }
  }, [isAdmin, session.token, weekStart])

  return (
    <div className="dashboard">
      <section className="card admin-card">
        <h1 className="title admin-title">Home Dashboard</h1>
        <p className="subtitle admin-sub">
          Welcome back, {session.profile.email} ({session.profile.role})
        </p>
        <p className="admin-hint" style={{ marginBottom: 0 }}>
          Use top navigation for full modules, and use quick actions below for the most common day-to-day tasks.
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
          <p className="kpi-label">Hours This Week</p>
          <p className="kpi-value">{kpis.loading ? '--' : kpis.weekHours.toFixed(2)}</p>
        </article>
      </section>

      <section className="dashboard-two-col">
        <article className="card admin-card quick-actions">
          <h2 className="admin-h2">Quick Actions</h2>
          <div className="quick-action-grid">
            <NavLink to="/timesheet" className="quick-action-tile qa-timesheet">
              <span className="quick-action-title">Add Timesheet Line</span>
              <span className="quick-action-sub">Log today&apos;s work quickly</span>
            </NavLink>
            <NavLink to="/expenses" className="quick-action-tile qa-projects">
              <span className="quick-action-title">Track Expense</span>
              <span className="quick-action-sub">Submit expenses for approval</span>
            </NavLink>
            <NavLink to="/clients" className="quick-action-tile qa-clients">
              <span className="quick-action-title">Create Client</span>
              <span className="quick-action-sub">Add a new customer profile</span>
            </NavLink>
            <NavLink to="/projects" className="quick-action-tile qa-projects">
              <span className="quick-action-title">Create Project</span>
              <span className="quick-action-sub">Start a new engagement</span>
            </NavLink>
            {isAdmin ? (
              <NavLink to="/admin/users" className="quick-action-tile qa-users">
                <span className="quick-action-title">Add User</span>
                <span className="quick-action-sub">Invite teammate + assign role</span>
              </NavLink>
            ) : null}
          </div>
        </article>

        <article className="card admin-card workspace-card">
          <h2 className="admin-h2">Workspace</h2>
          <ul className="dashboard-list dashboard-links">
            <li>
              <NavLink to="/timesheet">Timesheet</NavLink>
            </li>
            <li>
              <NavLink to="/expenses">Expenses</NavLink>
            </li>
            <li>
              <NavLink to="/clients">Clients</NavLink>
            </li>
            <li>
              <NavLink to="/projects">Projects</NavLink>
            </li>
            {isAdmin ? (
              <li>
                <NavLink to="/admin/users">User Management</NavLink>
              </li>
            ) : null}
          </ul>
        </article>
      </section>

      <section className="card admin-card">
        <h2 className="admin-h2">Current Delivery Scope</h2>
        <ul className="dashboard-list">
          <li>Authentication and role-based access controls</li>
          <li>Timesheet weekly entry workflow</li>
          <li>Client management directory</li>
          <li>Project management directory with filters and edits</li>
          {isAdmin ? <li>User administration and role assignment</li> : null}
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
      <div className="card">
        <h1 className="title">C2E</h1>
        <p className="subtitle">Sign in with your work email</p>
        <form className="form" onSubmit={onSubmit}>
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
            {busy ? 'Signing in...' : 'Sign in'}
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
  const isAdmin = session.profile.role === 'Admin'
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
        <div className="topbar-brand">C2E</div>
        <nav className="topbar-tabs" aria-label="Primary navigation">
          <NavLink to="/" end className={({ isActive }) => `topbar-tab${isActive ? ' active' : ''}`}>
            Home
          </NavLink>
          <NavLink to="/timesheet" className={({ isActive }) => `topbar-tab${isActive ? ' active' : ''}`}>
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
          {isAdmin ? (
            <NavLink to="/admin/users" className={({ isActive }) => `topbar-tab${isActive ? ' active' : ''}`}>
              User Management
            </NavLink>
          ) : null}
        </nav>
        <div className="topbar-user">
          <button type="button" className="btn secondary btn-sm" onClick={toggleDensity}>
            {density === 'comfortable' ? 'Compact view' : 'Comfortable view'}
          </button>
          <span>{session.profile.email}</span>
          <button type="button" className="btn secondary btn-sm" onClick={onSignOut}>
            Sign out
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

function AppRoutes() {
  const [session, setSession] = useState<Session | null>(null)
  const signOut = useCallback(() => setSession(null), [])
  return (
    <Routes>
      <Route path="/login" element={<LoginPage onSignedIn={setSession} />} />
      <Route element={<AuthenticatedLayout session={session} onSignOut={signOut} />}>
        <Route path="/" element={session ? <HomeDashboard session={session} /> : <Navigate to="/login" replace />} />
        <Route path="/admin/users" element={<AdminUsersRoute session={session} onSignOut={signOut} />} />
        <Route path="/timesheet" element={<TimesheetRoute session={session} />} />
        <Route path="/expenses" element={<ExpensesRoute session={session} />} />
        <Route path="/clients" element={<ClientsRoute session={session} />} />
        <Route path="/projects" element={<ProjectsRoute session={session} />} />
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
