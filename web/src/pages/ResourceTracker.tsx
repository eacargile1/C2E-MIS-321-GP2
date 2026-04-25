import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Link, useNavigate, useOutletContext } from 'react-router-dom'
import AssignmentManager from '../components/AssignmentManager'
import ResourceTrackerPartnerProjectRoles from '../components/ResourceTrackerPartnerProjectRoles'
import {
  assignEmployeeToClient,
  assignEmployeeToProject,
  getProjectStaffingRecommendations,
  getResourceTrackerMonth,
  listAssignableEmployees,
  listClientAssignments,
  listClients,
  listProjectAssignments,
  listProjects,
  unassignEmployeeFromClient,
  unassignEmployeeFromProject,
  type ClientRow,
  type MeProfile,
  type ProjectRow,
  type ResourceTrackerEmployeeRow,
} from '../api'
import { clampTimesheetWeekMondayYmd, parseYmdLocal, startOfWeekMonday, toYmd } from '../timesheetNavWindow'
import '../App.css'

type Toast = { id: number; message: string; variant: 'ok' | 'err' }

const TOAST_MS = 4000

function addDays(d: Date, days: number) {
  const x = new Date(d.getTime())
  x.setDate(x.getDate() + days)
  return x
}

function startOfMonth(d: Date) {
  return new Date(d.getFullYear(), d.getMonth(), 1)
}

function endOfMonth(d: Date) {
  return new Date(d.getFullYear(), d.getMonth() + 1, 0)
}

type ResourceTrackerOutletSession = { token: string; profile: MeProfile }

export default function ResourceTracker() {
  const session = useOutletContext<ResourceTrackerOutletSession | null>()
  if (!session) {
    return (
      <div className="admin-wrap">
        <p className="admin-hint">Open Resource tracker from the main navigation.</p>
      </div>
    )
  }
  const { token, profile } = session
  const nav = useNavigate()
  /** Only Partners use this page as a staffing console; everyone else sees a read-only grid. */
  const canInteractAsPartner = profile.role === 'Partner'
  const canManageAssignments = canInteractAsPartner

  const [monthAnchor, setMonthAnchor] = useState(() => startOfMonth(new Date()))
  const [monthRows, setMonthRows] = useState<ResourceTrackerEmployeeRow[]>([])
  const [monthLoading, setMonthLoading] = useState(true)
  const [toasts, setToasts] = useState<Toast[]>([])
  const lastMonthLoadId = useRef(0)

  const [clientRows, setClientRows] = useState<ClientRow[]>([])
  const [projectRows, setProjectRows] = useState<ProjectRow[]>([])
  const [targetsLoading, setTargetsLoading] = useState(false)
  const [highlightedUserId, setHighlightedUserId] = useState<string | null>(null)
  const staffingSectionRef = useRef<HTMLDivElement | null>(null)

  const monthLabel = useMemo(
    () => monthAnchor.toLocaleDateString(undefined, { month: 'long', year: 'numeric' }),
    [monthAnchor],
  )
  const monthStart = useMemo(() => startOfMonth(monthAnchor), [monthAnchor])
  const monthEnd = useMemo(() => endOfMonth(monthAnchor), [monthAnchor])
  const monthStartYmd = useMemo(() => toYmd(monthStart), [monthStart])
  const monthDays = useMemo(() => {
    const out: Date[] = []
    for (let d = monthStart; d <= monthEnd; d = addDays(d, 1)) out.push(d)
    return out
  }, [monthStart, monthEnd])

  const highlightedEmail = useMemo(() => {
    if (!highlightedUserId) return null
    const row = monthRows.find((r) => r.userId === highlightedUserId)
    return row?.email ?? null
  }, [highlightedUserId, monthRows])

  const pushToast = useCallback((message: string, variant: 'ok' | 'err') => {
    const id = Date.now()
    setToasts((t) => [...t, { id, message, variant }])
    window.setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), TOAST_MS)
  }, [])

  const refreshMonth = useCallback(async () => {
    const loadId = ++lastMonthLoadId.current
    setMonthLoading(true)
    try {
      const rows = await getResourceTrackerMonth(token, monthStartYmd)
      if (loadId !== lastMonthLoadId.current) return
      setMonthRows(rows)
    } catch (e) {
      if (loadId !== lastMonthLoadId.current) return
      pushToast(e instanceof Error ? e.message : 'Month load failed', 'err')
      setMonthRows([])
    } finally {
      if (loadId === lastMonthLoadId.current) setMonthLoading(false)
    }
  }, [monthStartYmd, token, pushToast])

  useEffect(() => {
    void refreshMonth()
  }, [refreshMonth])

  const loadAssignmentTargets = useCallback(async () => {
    if (!canManageAssignments) return
    setTargetsLoading(true)
    try {
      const [clients, projects] = await Promise.all([
        listClients(token, undefined, false),
        listProjects(token, { includeInactive: false }),
      ])
      setClientRows(clients.filter((c) => c.isActive))
      setProjectRows(projects.filter((p) => p.isActive))
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Could not load clients/projects for staffing', 'err')
      setClientRows([])
      setProjectRows([])
    } finally {
      setTargetsLoading(false)
    }
  }, [canManageAssignments, token, pushToast])

  useEffect(() => {
    void loadAssignmentTargets()
  }, [loadAssignmentTargets])

  const setPrevMonth = () => setMonthAnchor((d) => new Date(d.getFullYear(), d.getMonth() - 1, 1))
  const setNextMonth = () => setMonthAnchor((d) => new Date(d.getFullYear(), d.getMonth() + 1, 1))
  const jumpToTodayMonth = () => setMonthAnchor(startOfMonth(new Date()))

  const openWeekForDay = (dayYmd: string) => {
    const d = parseYmdLocal(dayYmd)
    if (!d) return
    const rawMonday = toYmd(startOfWeekMonday(d))
    const { ymd, didClamp } = clampTimesheetWeekMondayYmd(rawMonday)
    if (didClamp)
      pushToast(
        'That week is outside the ±1 month timesheet entry window (server uses UTC); opened the nearest allowed week.',
        'err',
      )
    nav(`/timesheet?week=${encodeURIComponent(ymd)}`)
  }

  const onPickEmployee = (userId: string) => {
    setHighlightedUserId(userId)
    window.requestAnimationFrame(() => {
      staffingSectionRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
    })
  }

  return (
    <div className="admin-wrap">
      <div className="card admin-card resource-page-header-card">
        <p className="resource-page-eyebrow">Resource Tracker</p>
        <h1 className="title admin-title">Resource Tracker</h1>
        <p className="subtitle admin-sub">
          Signed in as {profile.displayName} · {profile.role}
        </p>
        <p className="admin-hint" style={{ marginTop: 8 }}>
          Org-wide view: each cell reflects <strong>hours logged in timesheets</strong> for that day.
          {canInteractAsPartner ? (
            <>
              {' '}
              As a <strong>Partner</strong>, click a <strong>day cell</strong> to open that week on your timesheet, or
              click an <strong>employee name</strong> to pre-select them in the staffing panels below.
            </>
          ) : (
            <>
              {' '}
              This grid is <strong>read-only</strong> for your role — use the <Link to="/timesheet">Time Tracking</Link> page
              to log or review your own hours. Staffing changes on this screen are limited to <strong>Partner</strong>{' '}
              accounts; admins use User Management and project pages for the same backend operations.
            </>
          )}
        </p>
        {canInteractAsPartner ? (
          <p className="admin-hint" style={{ marginTop: 8 }}>
            Use the sections below for <strong>client</strong> and <strong>project</strong> employee assignments and{' '}
            <strong>Project Delivery Roles &amp; Team</strong> (DM / EP / finance / roster). Org manager (reports-to) is
            set in <Link to="/admin/users">User Management</Link> when an admin creates or edits a user. Per-line task text on
            timesheets is still entered on each person&apos;s weekly timesheet. You
            can also use <Link to="/clients">Clients</Link> and <Link to="/projects">Projects</Link> for the same
            assignment APIs.
          </p>
        ) : null}
      </div>

      <div className="card admin-card resource-section-card">
        <div className="admin-table-head resource-section-head">
          <div>
            <h2 className="admin-h2 resource-section-title">Monthly Availability Grid</h2>
            <p className="admin-hint resource-section-subtitle">{monthLabel}</p>
          </div>
          <div className="admin-header-actions resource-month-nav">
            <button type="button" className="btn secondary btn-sm" onClick={setPrevMonth} disabled={monthLoading}>
              ← Month
            </button>
            <button type="button" className="btn secondary btn-sm" onClick={setNextMonth} disabled={monthLoading}>
              Month →
            </button>
            <button type="button" className="btn secondary btn-sm" onClick={jumpToTodayMonth} disabled={monthLoading}>
              This Month
            </button>
            <button type="button" className="btn secondary btn-sm" onClick={() => void refreshMonth()} disabled={monthLoading}>
              Refresh
            </button>
          </div>
        </div>
        <div className="timesheet-legend resource-legend">
          <span>
            <i className="lg fully" /> Fully Booked
          </span>
          <span>
            <i className="lg soft" /> Soft Booked
          </span>
          <span>
            <i className="lg available" /> Available
          </span>
          <span>
            <i className="lg pto" /> PTO
          </span>
        </div>
        {monthLoading ? (
          <p className="admin-hint">Loading month…</p>
        ) : (
          <div className="table-scroll">
            <table
              className={`resource-matrix${!canInteractAsPartner ? ' resource-tracker-readonly' : ''}`}
            >
              <thead>
                <tr>
                  <th className="sticky-col">Employee</th>
                  {monthDays.map((d) => (
                    <th key={toYmd(d)}>{d.getDate()}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {monthRows.length === 0 ? (
                  <tr>
                    <td className="sticky-col">No Employees</td>
                    {monthDays.map((d) => (
                      <td key={toYmd(d)} className="status-empty">
                        —
                      </td>
                    ))}
                  </tr>
                ) : (
                  monthRows.map((row) => (
                    <tr
                      key={row.userId}
                      className={highlightedUserId === row.userId ? 'resource-tracker-row-focused' : undefined}
                    >
                      <td
                        className={`sticky-col${canInteractAsPartner ? ' resource-tracker-employee-pick' : ''}`}
                        title={
                          canInteractAsPartner
                            ? `${row.email} (${row.role}) — click to pre-select for staffing below`
                            : `${row.email} (${row.role})`
                        }
                        role={canInteractAsPartner ? 'button' : undefined}
                        tabIndex={canInteractAsPartner ? 0 : undefined}
                        onClick={
                          canInteractAsPartner
                            ? (e) => {
                                e.stopPropagation()
                                onPickEmployee(row.userId)
                              }
                            : undefined
                        }
                        onKeyDown={
                          canInteractAsPartner
                            ? (e) => {
                                if (e.key === 'Enter' || e.key === ' ') {
                                  e.preventDefault()
                                  onPickEmployee(row.userId)
                                }
                              }
                            : undefined
                        }
                      >
                        {row.email}
                      </td>
                      {row.days.map((day) => (
                        <td
                          key={day.date}
                          className={`status-${day.status}`}
                          title={
                            canInteractAsPartner
                              ? `${row.email} · ${day.date} · ${day.status} · ${day.hours.toFixed(2)}h — click to open Time Tracking`
                              : `${row.email} · ${day.date} · ${day.status} · ${day.hours.toFixed(2)}h`
                          }
                          role={canInteractAsPartner ? 'button' : undefined}
                          tabIndex={canInteractAsPartner ? 0 : undefined}
                          onClick={canInteractAsPartner ? () => openWeekForDay(day.date) : undefined}
                          onKeyDown={
                            canInteractAsPartner
                              ? (e) => {
                                  if (e.key === 'Enter' || e.key === ' ') {
                                    e.preventDefault()
                                    openWeekForDay(day.date)
                                  }
                                }
                              : undefined
                          }
                        >
                          {day.hours > 0 ? day.hours.toFixed(0) : ''}
                        </td>
                      ))}
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {canManageAssignments ? (
        <div ref={staffingSectionRef} id="resource-tracker-staffing">
          {highlightedUserId && highlightedEmail ? (
            <div className="card admin-card" style={{ marginBottom: 12 }}>
              <p className="admin-hint" style={{ marginBottom: 0 }}>
                Staffing focus: <strong>{highlightedEmail}</strong> — pre-selected in each panel when they are still
                unassigned to that client or project.{' '}
                <button type="button" className="btn secondary btn-sm" onClick={() => setHighlightedUserId(null)}>
                  Clear Focus
                </button>
              </p>
            </div>
          ) : null}
          {targetsLoading ? (
            <p className="admin-hint">Loading clients and projects for staffing…</p>
          ) : null}
          <AssignmentManager
            token={token}
            title="Client Staffing (From Resource Tracker)"
            targetLabel="Client"
            targetOptions={clientRows.map((c) => ({ id: c.id, name: c.name, isActive: c.isActive }))}
            loadAssignableEmployees={listAssignableEmployees}
            loadAssignments={listClientAssignments}
            assign={assignEmployeeToClient}
            unassign={unassignEmployeeFromClient}
            canManage={canManageAssignments}
            preferredUserId={highlightedUserId}
          />
          <AssignmentManager
            token={token}
            title="Project Staffing (From Resource Tracker)"
            targetLabel="Project"
            targetOptions={projectRows.map((p) => ({
              id: p.id,
              name: `${p.clientName} — ${p.name}`,
              isActive: p.isActive,
            }))}
            loadAssignableEmployees={listAssignableEmployees}
            loadAssignments={listProjectAssignments}
            assign={assignEmployeeToProject}
            unassign={unassignEmployeeFromProject}
            loadRecommendations={getProjectStaffingRecommendations}
            canManage={canManageAssignments}
            preferredUserId={highlightedUserId}
          />
          <ResourceTrackerPartnerProjectRoles token={token} projects={projectRows} />
        </div>
      ) : null}

      <div className="toast-stack" aria-live="polite">
        {toasts.map((t) => (
          <div key={t.id} className={`toast toast-${t.variant}`}>
            {t.message}
          </div>
        ))}
      </div>
    </div>
  )
}
