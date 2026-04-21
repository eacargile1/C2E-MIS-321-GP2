import { useCallback, useEffect, useMemo, useState } from 'react'
import { getProject, listProjectStaffingUsers, patchProject, type ProjectRow, type ProjectStaffingUserRow } from '../api'

type Toast = { id: number; message: string; variant: 'ok' | 'err' }
const TOAST_MS = 4000

export default function ResourceTrackerPartnerProjectRoles({
  token,
  projects,
}: {
  token: string
  projects: ProjectRow[]
}) {
  const [projectId, setProjectId] = useState('')
  const [project, setProject] = useState<ProjectRow | null>(null)
  const [staff, setStaff] = useState<ProjectStaffingUserRow[]>([])
  const [loading, setLoading] = useState(false)
  const [staffDm, setStaffDm] = useState('')
  const [staffEp, setStaffEp] = useState('')
  const [staffFin, setStaffFin] = useState('')
  const [teamDraft, setTeamDraft] = useState<string[]>([])
  const [saving, setSaving] = useState(false)
  const [toasts, setToasts] = useState<Toast[]>([])

  const pushToast = useCallback((message: string, variant: 'ok' | 'err') => {
    const id = Date.now()
    setToasts((t) => [...t, { id, message, variant }])
    window.setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), TOAST_MS)
  }, [])

  const activeProjects = useMemo(() => projects.filter((p) => p.isActive), [projects])

  useEffect(() => {
    if (activeProjects.length === 0) {
      setProjectId('')
      return
    }
    setProjectId((prev) => (prev && activeProjects.some((p) => p.id === prev) ? prev : activeProjects[0]!.id))
  }, [activeProjects])

  const loadProject = useCallback(async () => {
    if (!projectId) {
      setProject(null)
      setStaff([])
      return
    }
    setLoading(true)
    try {
      const [p, s] = await Promise.all([getProject(token, projectId), listProjectStaffingUsers(token)])
      setProject(p)
      setStaff(s)
      setStaffDm(p.deliveryManagerUserId ?? '')
      setStaffEp(p.engagementPartnerUserId ?? '')
      setStaffFin(p.assignedFinanceUserId ?? '')
      setTeamDraft([...(p.teamMemberUserIds ?? [])])
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Could not load project', 'err')
      setProject(null)
      setStaff([])
    } finally {
      setLoading(false)
    }
  }, [projectId, token, pushToast])

  useEffect(() => {
    void loadProject()
  }, [loadProject])

  const managers = useMemo(() => staff.filter((u) => u.role === 'Manager'), [staff])
  const partners = useMemo(() => staff.filter((u) => u.role === 'Partner'), [staff])
  const financeUsers = useMemo(() => staff.filter((u) => u.role === 'Finance'), [staff])
  const staffSortedForTeam = useMemo(
    () => [...staff].sort((a, b) => (a.email || '').localeCompare(b.email || '', undefined, { sensitivity: 'base' })),
    [staff],
  )

  const toggleTeam = (userId: string) => {
    setTeamDraft((prev) => (prev.includes(userId) ? prev.filter((x) => x !== userId) : [...prev, userId]))
  }

  const onSaveStaffing = async () => {
    if (!project) return
    setSaving(true)
    try {
      await patchProject(token, project.id, {
        ...(staffDm ? { deliveryManagerUserId: staffDm } : { clearDeliveryManager: true }),
        ...(staffEp ? { engagementPartnerUserId: staffEp } : { clearEngagementPartner: true }),
        ...(staffFin ? { assignedFinanceUserId: staffFin } : { clearAssignedFinance: true }),
        teamMemberUserIds: teamDraft,
      })
      pushToast('Project staffing updated', 'ok')
      await loadProject()
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Save failed', 'err')
    } finally {
      setSaving(false)
    }
  }

  if (activeProjects.length === 0) {
    return (
      <div className="card admin-card">
        <h2 className="admin-h2">Project Delivery Roles &amp; Team</h2>
        <p className="admin-hint">No active projects to configure.</p>
      </div>
    )
  }

  return (
    <div className="card admin-card">
      <div className="admin-table-head">
        <h2 className="admin-h2">Project Delivery Roles &amp; Team</h2>
        <button type="button" className="btn secondary btn-sm" onClick={() => void loadProject()} disabled={loading || saving}>
          Refresh
        </button>
      </div>
      <p className="admin-hint">
        Same fields as Project Detail — Delivery Manager, Engagement Partner, Assigned Finance, and Team Roster.
      </p>
      <label className="field" style={{ maxWidth: 480, marginTop: 12 }}>
        <span>Project</span>
        <select value={projectId} onChange={(e) => setProjectId(e.target.value)}>
          {activeProjects.map((p) => (
            <option key={p.id} value={p.id}>
              {p.clientName} — {p.name}
            </option>
          ))}
        </select>
      </label>
      {loading || !project ? (
        <p className="admin-hint" style={{ marginTop: 12 }}>
          {loading ? 'Loading…' : '—'}
        </p>
      ) : (
        <>
        <div className="form admin-form-grid" style={{ marginTop: 16, maxWidth: 560 }}>
          <label className="field">
            <span>Delivery Manager</span>
            <select value={staffDm} onChange={(e) => setStaffDm(e.target.value)}>
              <option value="">— None</option>
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
          <div style={{ gridColumn: '1 / -1' }}>
            <h3 className="admin-h3" style={{ margin: '12px 0 6px', fontSize: '1rem' }}>
              Team Roster
            </h3>
            <div
              className="admin-hint"
              style={{
                maxHeight: 200,
                overflowY: 'auto',
                border: '1px solid var(--border, #ddd)',
                borderRadius: 8,
                padding: 8,
              }}
            >
              {staffSortedForTeam.length === 0 ? (
                <span>No staffing directory users.</span>
              ) : (
                staffSortedForTeam.map((u) => (
                  <label key={u.id} style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '4px 0' }}>
                    <input type="checkbox" checked={teamDraft.includes(u.id)} onChange={() => toggleTeam(u.id)} />
                    <span>
                      {u.displayName || u.email} <span className="admin-hint">({u.role})</span>
                    </span>
                  </label>
                ))
              )}
            </div>
          </div>
        </div>
        <div
          style={{
            marginTop: 20,
            paddingTop: 16,
            borderTop: '1px solid var(--border, #e5e7eb)',
            display: 'flex',
            justifyContent: 'flex-end',
          }}
        >
          <button type="button" className="btn primary" disabled={saving} onClick={() => void onSaveStaffing()}>
            {saving ? 'Saving…' : 'Save Changes'}
          </button>
        </div>
        </>
      )}
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
