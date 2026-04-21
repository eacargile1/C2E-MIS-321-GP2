import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  assignEmployeeToProject,
  createProject,
  getProjectStaffingRecommendations,
  listAssignableEmployees,
  listClients,
  listProjectAssignments,
  listProjects,
  listProjectStaffingUsers,
  patchProject,
  unassignEmployeeFromProject,
  type ClientRow,
  type MeProfile,
  type ProjectRow,
  type ProjectStaffingUserRow,
} from '../api'
import AssignmentManager from '../components/AssignmentManager'
import '../App.css'

type Toast = { id: number; message: string; variant: 'ok' | 'err' }
const TOAST_MS = 4000
const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })

export default function ProjectsPage({
  token,
  profile,
}: {
  token: string
  profile: MeProfile
}) {
  const canCreateProject = profile.role === 'Admin' || profile.role === 'Partner'
  const canEditProjectInline = profile.role === 'Admin' || profile.role === 'Partner'
  const canShowInactive = profile.role === 'Admin'
  const canManageAssignments = profile.role === 'Admin' || profile.role === 'Partner'

  const [rows, setRows] = useState<ProjectRow[]>([])
  const [clients, setClients] = useState<ClientRow[]>([])
  const [staffUsers, setStaffUsers] = useState<ProjectStaffingUserRow[]>([])
  const [loading, setLoading] = useState(true)
  const [q, setQ] = useState('')
  const [clientFilter, setClientFilter] = useState('')
  const [includeInactive, setIncludeInactive] = useState(false)
  const [toasts, setToasts] = useState<Toast[]>([])
  const [createName, setCreateName] = useState('')
  const [createClientId, setCreateClientId] = useState('')
  const [createBudget, setCreateBudget] = useState('')
  const [createDmId, setCreateDmId] = useState('')
  const [createEpId, setCreateEpId] = useState('')
  const [createFinId, setCreateFinId] = useState('')
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editName, setEditName] = useState('')
  const [editClientId, setEditClientId] = useState('')
  const [editBudget, setEditBudget] = useState('')
  const [editActive, setEditActive] = useState(true)

  const managers = useMemo(() => staffUsers.filter((u) => u.role === 'Manager'), [staffUsers])
  const partners = useMemo(() => staffUsers.filter((u) => u.role === 'Partner'), [staffUsers])
  const financeUsers = useMemo(() => staffUsers.filter((u) => u.role === 'Finance'), [staffUsers])

  const pushToast = useCallback((message: string, variant: 'ok' | 'err') => {
    const id = Date.now()
    setToasts((t) => [...t, { id, message, variant }])
    window.setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), TOAST_MS)
  }, [])

  useEffect(() => {
    if (!canCreateProject) {
      setStaffUsers([])
      return
    }
    let cancelled = false
    void (async () => {
      try {
        const s = await listProjectStaffingUsers(token)
        if (!cancelled) setStaffUsers(s)
      } catch (e) {
        if (!cancelled) {
          setStaffUsers([])
          pushToast(e instanceof Error ? e.message : 'Could not load staffing pickers', 'err')
        }
      }
    })()
    return () => {
      cancelled = true
    }
  }, [token, canCreateProject, pushToast])

  useEffect(() => {
    if (!canCreateProject || profile.role !== 'Partner') return
    if (staffUsers.length === 0) return
    setCreateEpId((prev) => {
      if (prev !== '') return prev
      return staffUsers.some((u) => u.id === profile.id) ? profile.id : prev
    })
  }, [canCreateProject, profile.id, profile.role, staffUsers])

  const refresh = useCallback(async () => {
    setLoading(true)
    try {
      const [projectRows, clientRows] = await Promise.all([
        listProjects(token, {
          q: q || undefined,
          clientId: clientFilter || undefined,
          includeInactive: canShowInactive && includeInactive,
        }),
        listClients(token, undefined, false),
      ])
      setRows(projectRows)
      setClients(clientRows)
      setCreateClientId((prev) => {
        if (clientRows.length === 0) return ''
        if (prev === '') return clientRows[0]!.id
        return clientRows.some((c) => c.id === prev) ? prev : clientRows[0]!.id
      })
    } catch (err) {
      pushToast(err instanceof Error ? err.message : 'Load failed', 'err')
    } finally {
      setLoading(false)
    }
  }, [token, q, clientFilter, canShowInactive, includeInactive, pushToast])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const onCreate = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!canCreateProject) return
    const budget = Number(createBudget)
    if (!Number.isFinite(budget)) {
      pushToast('Budget must be a number.', 'err')
      return
    }
    try {
      await createProject(token, {
        name: createName.trim(),
        clientId: createClientId,
        budgetAmount: budget,
        deliveryManagerUserId: createDmId || undefined,
        engagementPartnerUserId: createEpId || undefined,
        assignedFinanceUserId: createFinId || undefined,
      })
      setCreateName('')
      setCreateBudget('')
      setCreateDmId('')
      setCreateEpId('')
      setCreateFinId('')
      pushToast('Project created', 'ok')
      await refresh()
    } catch (err) {
      pushToast(err instanceof Error ? err.message : 'Create failed', 'err')
    }
  }

  const startEdit = (p: ProjectRow) => {
    setEditingId(p.id)
    setEditName(p.name)
    setEditClientId(p.clientId)
    setEditBudget(String(p.budgetAmount))
    setEditActive(p.isActive)
  }

  const saveEdit = async (id: string) => {
    if (!canEditProjectInline) return
    const budget = Number(editBudget)
    if (!Number.isFinite(budget)) {
      pushToast('Budget must be a number.', 'err')
      return
    }
    try {
      await patchProject(token, id, {
        name: editName.trim(),
        clientId: editClientId,
        budgetAmount: budget,
        isActive: editActive,
      })
      setEditingId(null)
      pushToast('Project updated', 'ok')
      await refresh()
    } catch (err) {
      pushToast(err instanceof Error ? err.message : 'Update failed', 'err')
    }
  }

  return (
    <div className="admin-wrap">
      <div className="card admin-card">
        <h1 className="title admin-title">Projects</h1>
        <p className="subtitle admin-sub">
          Signed in as {profile.email} · {profile.role}
        </p>
        <p className="admin-hint" style={{ marginBottom: 0 }}>
          New engagements are created by Admin or Partner (optional staffing on create). Delivery manager, engagement
          partner, and assigned finance are maintained on each project&apos;s detail page. Managers and Finance use that
          page for visibility; Finance updates budget only on projects they are assigned to.
        </p>
      </div>

      <div className="card admin-card">
        <div className="admin-table-head">
          <h2 className="admin-h2">Directory</h2>
          <button type="button" className="btn secondary btn-sm" onClick={() => void refresh()} disabled={loading}>
            Refresh
          </button>
        </div>
        <div className="form admin-form-grid" style={{ marginBottom: 16 }}>
          <label className="field">
            <span>Search Name</span>
            <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Contains..." />
          </label>
          <label className="field">
            <span>Filter Client</span>
            <select value={clientFilter} onChange={(e) => setClientFilter(e.target.value)}>
              <option value="">All Clients</option>
              {clients.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
          </label>
          {canShowInactive ? (
            <label className="field" style={{ flexDirection: 'row', alignItems: 'center', gap: 8 }}>
              <input
                type="checkbox"
                checked={includeInactive}
                onChange={(e) => setIncludeInactive(e.target.checked)}
              />
              <span>Show Inactive</span>
            </label>
          ) : null}
        </div>
      </div>

      {canCreateProject ? (
        <div className="card admin-card">
          <h2 className="admin-h2">New Project</h2>
          <form className="form admin-form-grid" onSubmit={onCreate}>
            <label className="field">
              <span>Name</span>
              <input value={createName} onChange={(e) => setCreateName(e.target.value)} required />
            </label>
            <label className="field">
              <span>Client</span>
              <select value={createClientId} onChange={(e) => setCreateClientId(e.target.value)} required>
                {clients.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </select>
            </label>
            <label className="field">
              <span>Budget</span>
              <input
                type="number"
                min={0}
                step="0.01"
                value={createBudget}
                onChange={(e) => setCreateBudget(e.target.value)}
                required
              />
            </label>
            <label className="field">
              <span>Delivery manager (optional)</span>
              <select value={createDmId} onChange={(e) => setCreateDmId(e.target.value)}>
                <option value="">—</option>
                {managers.map((u) => (
                  <option key={u.id} value={u.id}>
                    {u.displayName || u.email}
                  </option>
                ))}
              </select>
            </label>
            <label className="field">
              <span>Engagement partner (optional)</span>
              <select value={createEpId} onChange={(e) => setCreateEpId(e.target.value)}>
                <option value="">—</option>
                {partners.map((u) => (
                  <option key={u.id} value={u.id}>
                    {u.displayName || u.email}
                  </option>
                ))}
              </select>
            </label>
            <label className="field">
              <span>Assigned finance (optional)</span>
              <select value={createFinId} onChange={(e) => setCreateFinId(e.target.value)}>
                <option value="">—</option>
                {financeUsers.map((u) => (
                  <option key={u.id} value={u.id}>
                    {u.displayName || u.email}
                  </option>
                ))}
              </select>
            </label>
            <button type="submit" className="btn primary">
              Create
            </button>
          </form>
        </div>
      ) : null}

      <div className="card admin-card">
        {loading ? (
          <p className="admin-hint">Loading...</p>
        ) : rows.length === 0 ? (
          <p className="admin-hint">No projects match.</p>
        ) : (
          <div className="table-scroll">
            <table className="admin-table">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Client</th>
                  <th>Budget</th>
                  <th>Status</th>
                  <th>View</th>
                  {canEditProjectInline ? <th /> : null}
                </tr>
              </thead>
              <tbody>
                {rows.map((p) => (
                  <tr key={p.id}>
                    <td>
                      {editingId === p.id ? (
                        <input className="table-input" value={editName} onChange={(e) => setEditName(e.target.value)} />
                      ) : (
                        p.name
                      )}
                    </td>
                    <td>
                      {editingId === p.id ? (
                        <select
                          className="table-input"
                          value={editClientId}
                          onChange={(e) => setEditClientId(e.target.value)}
                        >
                          {clients.map((c) => (
                            <option key={c.id} value={c.id}>
                              {c.name}
                            </option>
                          ))}
                        </select>
                      ) : (
                        p.clientName
                      )}
                    </td>
                    <td>
                      {editingId === p.id ? (
                        <input
                          className="table-input"
                          type="number"
                          min={0}
                          step="0.01"
                          value={editBudget}
                          onChange={(e) => setEditBudget(e.target.value)}
                        />
                      ) : (
                        usd.format(p.budgetAmount)
                      )}
                    </td>
                    <td>
                      {editingId === p.id ? (
                        <label style={{ display: 'flex', alignItems: 'center', gap: 6, whiteSpace: 'nowrap' }}>
                          <input type="checkbox" checked={editActive} onChange={(e) => setEditActive(e.target.checked)} />
                          active
                        </label>
                      ) : p.isActive ? (
                        'Active'
                      ) : (
                        'Inactive'
                      )}
                    </td>
                    <td>
                      <Link to={`/projects/${p.id}`} className="btn secondary btn-sm" style={{ textDecoration: 'none' }}>
                        Open
                      </Link>
                    </td>
                    {canEditProjectInline ? (
                      <td>
                        {editingId === p.id ? (
                          <>
                            <button type="button" className="btn primary btn-sm" onClick={() => void saveEdit(p.id)}>
                              Save
                            </button>{' '}
                            <button type="button" className="btn secondary btn-sm" onClick={() => setEditingId(null)}>
                              Cancel
                            </button>
                          </>
                        ) : (
                          <button type="button" className="btn secondary btn-sm" onClick={() => startEdit(p)}>
                            Edit
                          </button>
                        )}
                      </td>
                    ) : null}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <AssignmentManager
        token={token}
        title="Project staffing assignments"
        targetLabel="Project"
        targetOptions={rows.map((p) => ({ id: p.id, name: p.name, isActive: p.isActive }))}
        loadAssignableEmployees={listAssignableEmployees}
        loadAssignments={listProjectAssignments}
        assign={assignEmployeeToProject}
        unassign={unassignEmployeeFromProject}
        loadRecommendations={getProjectStaffingRecommendations}
        canManage={canManageAssignments}
      />

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
