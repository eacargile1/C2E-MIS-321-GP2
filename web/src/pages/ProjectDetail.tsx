import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import {
  getProject,
  getProjectExpenseInsights,
  listProjectStaffingUsers,
  patchProject,
  type MeProfile,
  type ProjectExpenseInsights,
  type ProjectRow,
  type ProjectStaffingUserRow,
} from '../api'
import '../App.css'

type Toast = { id: number; message: string; variant: 'ok' | 'err' }
const TOAST_MS = 4000
const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })

function labelForStaff(
  id: string | null | undefined,
  staff: ProjectStaffingUserRow[],
): string {
  if (!id) return '—'
  const u = staff.find((s) => s.id === id)
  return u ? `${u.displayName || u.email} (${u.role})` : id.slice(0, 8) + '…'
}

export default function ProjectDetailPage({ token, profile }: { token: string; profile: MeProfile }) {
  const { projectId } = useParams()
  const id = projectId?.trim() ?? ''
  const [project, setProject] = useState<ProjectRow | null>(null)
  const [insights, setInsights] = useState<ProjectExpenseInsights | null>(null)
  const [staff, setStaff] = useState<ProjectStaffingUserRow[]>([])
  const [loading, setLoading] = useState(true)
  const [insightsErr, setInsightsErr] = useState<string | null>(null)
  const [toasts, setToasts] = useState<Toast[]>([])
  const [budgetEdit, setBudgetEdit] = useState('')
  const [savingBudget, setSavingBudget] = useState(false)
  const [staffDm, setStaffDm] = useState('')
  const [staffEp, setStaffEp] = useState('')
  const [staffFin, setStaffFin] = useState('')
  const [savingStaffing, setSavingStaffing] = useState(false)
  const [teamMemberDraft, setTeamMemberDraft] = useState<string[]>([])
  const [savingTeam, setSavingTeam] = useState(false)

  const isIc = profile.role === 'IC'
  const canSeeExpenseRollup = true
  const canSeeStaffingLabels = true
  const canEditBudgetFinanceOnly = profile.role === 'Finance'
  const canEditStaffing = profile.role === 'Admin' || profile.role === 'Partner'

  const managers = useMemo(() => staff.filter((u) => u.role === 'Manager'), [staff])
  const partners = useMemo(() => staff.filter((u) => u.role === 'Partner'), [staff])
  const financeUsers = useMemo(() => staff.filter((u) => u.role === 'Finance'), [staff])
  const staffSortedForTeam = useMemo(
    () => [...staff].sort((a, b) => (a.email || '').localeCompare(b.email || '', undefined, { sensitivity: 'base' })),
    [staff],
  )

  const pushToast = useCallback((message: string, variant: 'ok' | 'err') => {
    const tid = Date.now()
    setToasts((t) => [...t, { id: tid, message, variant }])
    window.setTimeout(() => setToasts((t) => t.filter((x) => x.id !== tid)), TOAST_MS)
  }, [])

  const load = useCallback(async () => {
    if (!id) {
      setLoading(false)
      return
    }
    setLoading(true)
    setInsightsErr(null)
    try {
      const p = await getProject(token, id)
      setProject(p)
      setBudgetEdit(String(p.budgetAmount))
      setStaffDm(p.deliveryManagerUserId ?? '')
      setStaffEp(p.engagementPartnerUserId ?? '')
      setStaffFin(p.assignedFinanceUserId ?? '')
      setTeamMemberDraft([...(p.teamMemberUserIds ?? [])])
      if (canSeeStaffingLabels) {
        try {
          const s = await listProjectStaffingUsers(token)
          setStaff(s)
        } catch {
          setStaff([])
        }
      } else {
        setStaff([])
      }
      if (canSeeExpenseRollup) {
        try {
          const ins = await getProjectExpenseInsights(token, id)
          setInsights(ins)
        } catch (e) {
          setInsights(null)
          setInsightsErr(e instanceof Error ? e.message : 'Could not load expenses')
        }
      } else {
        setInsights(null)
      }
    } catch (e) {
      setProject(null)
      pushToast(e instanceof Error ? e.message : 'Load failed', 'err')
    } finally {
      setLoading(false)
    }
  }, [token, id, canSeeExpenseRollup, canSeeStaffingLabels, pushToast])

  useEffect(() => {
    void load()
  }, [load])

  const statusStyle = useMemo(
    () =>
      ({
        Pending: 'status-pending',
        Approved: 'status-approved',
        Rejected: 'status-denied',
      }) as Record<string, string>,
    [],
  )

  const toggleTeamMember = (userId: string) => {
    setTeamMemberDraft((prev) => (prev.includes(userId) ? prev.filter((x) => x !== userId) : [...prev, userId]))
  }

  const onSaveTeamRoster = async () => {
    if (!project || !canEditStaffing) return
    setSavingTeam(true)
    try {
      await patchProject(token, project.id, { teamMemberUserIds: teamMemberDraft })
      pushToast('Team roster updated', 'ok')
      void load()
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Team save failed', 'err')
    } finally {
      setSavingTeam(false)
    }
  }

  const onSaveStaffing = async () => {
    if (!project || !canEditStaffing) return
    setSavingStaffing(true)
    try {
      await patchProject(token, project.id, {
        ...(staffDm ? { deliveryManagerUserId: staffDm } : { clearDeliveryManager: true }),
        ...(staffEp ? { engagementPartnerUserId: staffEp } : { clearEngagementPartner: true }),
        ...(staffFin ? { assignedFinanceUserId: staffFin } : { clearAssignedFinance: true }),
      })
      pushToast('Staffing updated', 'ok')
      void load()
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Staffing save failed', 'err')
    } finally {
      setSavingStaffing(false)
    }
  }

  const onSaveBudget = async () => {
    if (!project || !canEditBudgetFinanceOnly) return
    const n = Number(budgetEdit)
    if (!Number.isFinite(n)) {
      pushToast('Budget must be a number', 'err')
      return
    }
    setSavingBudget(true)
    try {
      const updated = await patchProject(token, project.id, { budgetAmount: n })
      setProject(updated)
      pushToast('Budget updated', 'ok')
      void load()
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Save failed', 'err')
    } finally {
      setSavingBudget(false)
    }
  }

  if (!id) {
    return (
      <div className="admin-wrap">
        <p className="admin-hint">Missing project id.</p>
        <Link to="/projects">Back to projects</Link>
      </div>
    )
  }

  return (
    <div className="admin-wrap">
      <div className="card admin-card">
        <p className="admin-hint" style={{ marginBottom: 8 }}>
          <Link to="/projects">← Projects</Link>
        </p>
        <h1 className="title admin-title">Project</h1>
        {loading ? (
          <p className="admin-hint">Loading…</p>
        ) : !project ? (
          <p className="admin-hint">Not found or no access.</p>
        ) : (
          <>
            <p className="subtitle admin-sub">
              <strong>{project.name}</strong> · {project.clientName}
            </p>
            <dl className="review-hero-dl" style={{ marginTop: 12 }}>
              <div>
                <dt>Budget</dt>
                <dd>{usd.format(project.budgetAmount)}</dd>
              </div>
              <div>
                <dt>Status</dt>
                <dd>{project.isActive ? 'Active' : 'Inactive'}</dd>
              </div>
            </dl>

            {canEditBudgetFinanceOnly ? (
              <div className="form admin-form-grid" style={{ marginTop: 16, maxWidth: 420 }}>
                <label className="field">
                  <span>Update budget (Finance — assigned projects only)</span>
                  <input
                    type="number"
                    min={0}
                    step="0.01"
                    value={budgetEdit}
                    onChange={(e) => setBudgetEdit(e.target.value)}
                  />
                </label>
                <button type="button" className="btn primary btn-sm" disabled={savingBudget} onClick={() => void onSaveBudget()}>
                  {savingBudget ? 'Saving…' : 'Save Budget'}
                </button>
              </div>
            ) : null}

            {canSeeStaffingLabels ? (
              <section style={{ marginTop: 20 }}>
                <h2 className="admin-h2">Project Staffing</h2>
                {canEditStaffing ? (
                  <>
                    <p className="admin-hint">
                      For hours/expenses booked to this client + project name: IC uses the project{' '}
                      <strong>Delivery Manager</strong> when set, otherwise the <strong>Engagement Partner</strong>,
                      otherwise the submitter&apos;s <strong>Org Manager</strong> (set on the user). Finance uses the
                      submitter&apos;s Reporting Partner. Manager and Partner timesheets use the Engagement Partner on
                      billable lines (must match who should see the week in their queue). Partner-created projects
                      default you as Engagement Partner if you leave that field blank on create.
                    </p>
                    <div className="form admin-form-grid" style={{ marginTop: 12, maxWidth: 520 }}>
                      <label className="field">
                        <span>Delivery Manager</span>
                        <select value={staffDm} onChange={(e) => setStaffDm(e.target.value)}>
                          <option value="">— None (IC: engagement partner or org manager next)</option>
                          {managers.map((u) => (
                            <option key={u.id} value={u.id}>
                              {u.displayName || u.email}
                            </option>
                          ))}
                        </select>
                      </label>
                      <label className="field">
                        <span>Engagement Partner</span>
                        <select value={staffEp} onChange={(e) => setStaffEp(e.target.value)}>
                          <option value="">— None</option>
                          {partners.map((u) => (
                            <option key={u.id} value={u.id}>
                              {u.displayName || u.email}
                            </option>
                          ))}
                        </select>
                      </label>
                      <label className="field">
                        <span>Assigned Finance</span>
                        <select value={staffFin} onChange={(e) => setStaffFin(e.target.value)}>
                          <option value="">— None</option>
                          {financeUsers.map((u) => (
                            <option key={u.id} value={u.id}>
                              {u.displayName || u.email}
                            </option>
                          ))}
                        </select>
                      </label>
                    </div>
                    <div
                      style={{
                        marginTop: 16,
                        paddingTop: 16,
                        borderTop: '1px solid var(--border, #e5e7eb)',
                        display: 'flex',
                        justifyContent: 'flex-end',
                      }}
                    >
                      <button
                        type="button"
                        className="btn primary"
                        disabled={savingStaffing}
                        onClick={() => void onSaveStaffing()}
                      >
                        {savingStaffing ? 'Saving…' : 'Save Staffing'}
                      </button>
                    </div>
                  </>
                ) : (
                  <>
                    <p className="admin-hint">Only Admin or Partner can change these assignments.</p>
                    <ul className="admin-hint" style={{ listStyle: 'disc', paddingLeft: 20 }}>
                      <li>Delivery Manager: {labelForStaff(project.deliveryManagerUserId ?? null, staff)}</li>
                      <li>Engagement Partner: {labelForStaff(project.engagementPartnerUserId ?? null, staff)}</li>
                      <li>Assigned Finance: {labelForStaff(project.assignedFinanceUserId ?? null, staff)}</li>
                    </ul>
                  </>
                )}
                <h2 className="admin-h2" style={{ marginTop: 24 }}>
                  Team Roster (Optional)
                </h2>
                <p className="admin-hint">
                  Who is associated with this engagement. Timesheets and expenses still use the{' '}
                  <strong>exact client and project name</strong> from this directory; the roster is for visibility and
                  your own process.
                </p>
                {canEditStaffing ? (
                  <>
                    <div
                      className="admin-hint"
                      style={{
                        maxHeight: 220,
                        overflowY: 'auto',
                        border: '1px solid var(--border, #ddd)',
                        borderRadius: 8,
                        padding: 8,
                        marginTop: 8,
                      }}
                    >
                      {staffSortedForTeam.length === 0 ? (
                        <span>No users loaded for pickers.</span>
                      ) : (
                        staffSortedForTeam.map((u) => (
                          <label
                            key={u.id}
                            style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '4px 0' }}
                          >
                            <input
                              type="checkbox"
                              checked={teamMemberDraft.includes(u.id)}
                              onChange={() => toggleTeamMember(u.id)}
                            />
                            <span>
                              {u.displayName || u.email} <span className="admin-hint">({u.role})</span>
                            </span>
                          </label>
                        ))
                      )}
                    </div>
                    <div
                      style={{
                        marginTop: 16,
                        paddingTop: 16,
                        borderTop: '1px solid var(--border, #e5e7eb)',
                        display: 'flex',
                        justifyContent: 'flex-end',
                      }}
                    >
                      <button type="button" className="btn primary" disabled={savingTeam} onClick={() => void onSaveTeamRoster()}>
                        {savingTeam ? 'Saving…' : 'Save Team Roster'}
                      </button>
                    </div>
                  </>
                ) : (
                  <ul className="admin-hint" style={{ listStyle: 'disc', paddingLeft: 20, marginTop: 8 }}>
                    {(project.teamMemberUserIds ?? []).length === 0 ? (
                      <li>—</li>
                    ) : (
                      (project.teamMemberUserIds ?? []).map((tid) => <li key={tid}>{labelForStaff(tid, staff)}</li>)
                    )}
                  </ul>
                )}
              </section>
            ) : null}

            {insightsErr ? (
              <p className="admin-hint" style={{ marginTop: 16, color: 'var(--danger, #b42318)' }}>
                {insightsErr}
              </p>
            ) : insights ? (
              <section style={{ marginTop: 24 }}>
                <h2 className="admin-h2">Expenses on this project</h2>
                <p className="admin-hint">
                  Totals: {insights.pendingCount} pending ({usd.format(insights.pendingAmount)}),{' '}
                  {insights.approvedCount} approved ({usd.format(insights.approvedAmount)}),{' '}
                  {insights.rejectedCount} denied ({usd.format(insights.rejectedAmount)}).
                </p>
                {insights.expenses.length > 0 ? (
                  <div className="table-scroll">
                    <table className="admin-table">
                      <thead>
                        <tr>
                          <th>Date</th>
                          <th>Who</th>
                          <th>Status</th>
                          <th>Amount</th>
                          <th>Category</th>
                          <th>Description</th>
                        </tr>
                      </thead>
                      <tbody>
                        {insights.expenses.map((e) => (
                          <tr key={e.id}>
                            <td>{e.expenseDate}</td>
                            <td>{e.submitterEmail}</td>
                            <td>
                              <span className={`status-badge ${statusStyle[e.status] ?? 'status-draft'}`}>
                                {e.status}
                              </span>
                            </td>
                            <td>{usd.format(e.amount)}</td>
                            <td>{e.category}</td>
                            <td>{e.description}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                ) : isIc &&
                  insights.pendingCount + insights.approvedCount + insights.rejectedCount > 0 ? (
                  <p className="admin-hint" style={{ marginTop: 12 }}>
                    IC view shows rollups only. Managers, partners, assigned finance, and admins see the line-level grid
                    (submitter, category, description).
                  </p>
                ) : (
                  <p className="admin-hint" style={{ marginTop: 12 }}>
                    No expenses yet tagged to this client + project name.
                  </p>
                )}
              </section>
            ) : null}
          </>
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
