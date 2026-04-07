import { useCallback, useState } from 'react'
import { BrowserRouter, Link, Navigate, Route, Routes, useNavigate } from 'react-router-dom'
import { login, me, type MeProfile } from './api'
import AdminUsers from './pages/AdminUsers'
import ClientsPage from './pages/Clients'
import TimesheetWeek from './pages/TimesheetWeek'
import './App.css'

export type Session = { token: string; profile: MeProfile }

function HomePage({
  session,
  onSignOut,
}: {
  session: Session | null
  onSignOut: () => void
}) {
  if (!session) return <Navigate to="/login" replace />

  return (
    <main className="shell">
      <div className="card">
        <h1 className="title">C2E</h1>
        <p className="subtitle">Signed in</p>
        <div className="signed-in">
          <p className="ok">
            {session.profile.email} · {session.profile.role}
          </p>
          <p className="hint">Token is kept in memory only (refresh clears session).</p>
          <p className="hint">
            <Link to="/timesheet">Open timesheet →</Link>
          </p>
          <p className="hint">
            <Link to="/clients">Clients →</Link>
          </p>
          {session.profile.role === 'Admin' ? (
            <p className="hint">
              <Link to="/admin/users">Open user management →</Link>
            </p>
          ) : null}
          <button type="button" className="btn secondary" onClick={onSignOut}>
            Sign out
          </button>
        </div>
      </div>
    </main>
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
        const session: Session = { token: tok.accessToken, profile }
        onSignedIn(session)
        if (profile.role === 'Admin') nav('/admin/users', { replace: true })
        else nav('/', { replace: true })
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
            <input
              type="email"
              autoComplete="username"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
            />
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
            {busy ? 'Signing in…' : 'Sign in'}
          </button>
        </form>
      </div>
    </main>
  )
}

function AdminUsersRoute({ session, onSignOut }: { session: Session | null; onSignOut: () => void }) {
  if (!session) return <Navigate to="/login" replace />
  if (session.profile.role !== 'Admin') return <Navigate to="/" replace />
  return <AdminUsers token={session.token} profile={session.profile} onSignOut={onSignOut} />
}

function TimesheetRoute({ session, onSignOut }: { session: Session | null; onSignOut: () => void }) {
  if (!session) return <Navigate to="/login" replace />
  return <TimesheetWeek token={session.token} profile={session.profile} onSignOut={onSignOut} />
}

function ClientsRoute({ session, onSignOut }: { session: Session | null; onSignOut: () => void }) {
  if (!session) return <Navigate to="/login" replace />
  return <ClientsPage token={session.token} profile={session.profile} onSignOut={onSignOut} />
}

function AppRoutes() {
  const [session, setSession] = useState<Session | null>(null)

  const signOut = useCallback(() => {
    setSession(null)
  }, [])

  return (
    <Routes>
      <Route path="/login" element={<LoginPage onSignedIn={setSession} />} />
      <Route
        path="/admin/users"
        element={<AdminUsersRoute session={session} onSignOut={signOut} />}
      />
      <Route path="/timesheet" element={<TimesheetRoute session={session} onSignOut={signOut} />} />
      <Route path="/clients" element={<ClientsRoute session={session} onSignOut={signOut} />} />
      <Route path="/" element={<HomePage session={session} onSignOut={signOut} />} />
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
