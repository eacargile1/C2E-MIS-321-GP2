import { useCallback, useEffect, useMemo, useState } from 'react'
import { createUser, deleteUser, listUsers, patchUser, type MeProfile, type UserRow } from '../api'
import '../App.css'

type Toast = { id: number; message: string; variant: 'ok' | 'err' }

const TOAST_MS = 4000

const APP_ROLES = ['IC', 'Manager', 'Partner', 'Finance', 'Admin'] as const

export default function AdminUsers({
  token,
  profile,
  onSignOut,
}: {
  token: string
  profile: MeProfile
  onSignOut: () => void
}) {
  const [users, setUsers] = useState<UserRow[]>([])
  const [loading, setLoading] = useState(true)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [toasts, setToasts] = useState<Toast[]>([])
  const [createEmail, setCreateEmail] = useState('')
  const [createPassword, setCreatePassword] = useState('')
  const [createDisplayName, setCreateDisplayName] = useState('')
  const [createRole, setCreateRole] = useState<string>('IC')
  const [createManagerId, setCreateManagerId] = useState('')
  const [createPartnerId, setCreatePartnerId] = useState('')
  const [createSkills, setCreateSkills] = useState('')
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editEmail, setEditEmail] = useState('')
  const [editDisplayName, setEditDisplayName] = useState('')
  const [editPassword, setEditPassword] = useState('')
  const [editRole, setEditRole] = useState('')
  const [editManagerId, setEditManagerId] = useState('')
  const [editPartnerId, setEditPartnerId] = useState('')
  const [editSkills, setEditSkills] = useState('')
  const [confirmDeactivateId, setConfirmDeactivateId] = useState<string | null>(null)
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null)

  const pushToast = useCallback((message: string, variant: 'ok' | 'err') => {
    const id = Date.now()
    setToasts((t) => [...t, { id, message, variant }])
    window.setTimeout(() => {
      setToasts((t) => t.filter((x) => x.id !== id))
    }, TOAST_MS)
  }, [])

  const refresh = useCallback(async () => {
    setLoading(true)
    try {
      const list = await listUsers(token)
      setUsers(list)
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Load failed', 'err')
    } finally {
      setLoading(false)
    }
  }, [token, pushToast])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const activeManagers = useMemo(() => users.filter((u) => u.role === 'Manager' && u.isActive), [users])
  const activePartners = useMemo(() => users.filter((u) => u.role === 'Partner' && u.isActive), [users])
  const createNeedsOrgManager =
    createRole === 'IC' || (createRole === 'Manager' && activeManagers.length > 0)
  const createNeedsReportingPartner = createRole === 'Manager' && activeManagers.length > 0

  const onCreate = async (e: React.FormEvent) => {
    e.preventDefault()
    if (createNeedsOrgManager && !createManagerId.trim()) {
      pushToast('Select an org manager (active Manager) for IC and Manager accounts.', 'err')
      return
    }
    if (createNeedsReportingPartner && !createPartnerId.trim()) {
      pushToast('Select a reporting partner (active Partner) when another Manager already exists.', 'err')
      return
    }
    if (createRole === 'Finance' && activePartners.length === 0 && !createPartnerId.trim()) {
      pushToast('Create at least one Partner user before adding Finance (or pick a reporting partner explicitly).', 'err')
      return
    }
    try {
      await createUser(token, createEmail.trim(), createPassword, {
        displayName: createDisplayName.trim() || undefined,
        managerUserId: createManagerId.trim().length ? createManagerId.trim() : undefined,
        partnerUserId: createPartnerId.trim().length ? createPartnerId.trim() : undefined,
        role: createRole,
        skills: parseSkillsCsv(createSkills),
      })
      setCreateEmail('')
      setCreatePassword('')
      setCreateDisplayName('')
      setCreateRole('IC')
      setCreateManagerId('')
      setCreatePartnerId('')
      setCreateSkills('')
      pushToast('User created', 'ok')
      await refresh()
    } catch (err) {
      pushToast(err instanceof Error ? err.message : 'Create failed', 'err')
    }
  }

  const startEdit = (u: UserRow) => {
    setEditingId(u.id)
    setEditEmail(u.email)
    setEditDisplayName(u.displayName)
    setEditPassword('')
    setEditRole(u.role)
    setEditManagerId(u.managerUserId ?? '')
    setEditPartnerId(u.partnerUserId ?? '')
    setEditSkills(u.skills.join(', '))
  }

  const cancelEdit = () => {
    setEditingId(null)
    setEditEmail('')
    setEditDisplayName('')
    setEditPassword('')
    setEditRole('')
    setEditManagerId('')
    setEditPartnerId('')
    setEditSkills('')
  }

  const saveEdit = async (id: string) => {
    const needsOrgMgr =
      editRole === 'IC' || (editRole === 'Manager' && activeManagers.filter((m) => m.id !== id).length > 0)
    const needsPartner = editRole === 'Manager' && activeManagers.filter((m) => m.id !== id).length > 0
    if (needsOrgMgr && !editManagerId.trim()) {
      pushToast('Select an org manager for IC and Manager accounts.', 'err')
      return
    }
    if (needsPartner && !editPartnerId.trim()) {
      pushToast('Select a reporting partner for Manager when another Manager exists.', 'err')
      return
    }
    if (editRole === 'Finance' && activePartners.length === 0 && !editPartnerId.trim()) {
      pushToast('Add a Partner user before assigning the Finance role (or pick a reporting partner explicitly).', 'err')
      return
    }
    if (editRole === 'Finance' && !editPartnerId.trim() && (users.find((x) => x.id === id)?.partnerUserId ?? '')) {
      pushToast('Finance accounts must keep a reporting partner; pick a Partner or cancel.', 'err')
      return
    }
    setBusyId(id)
    try {
      const body: {
        email?: string
        password?: string
        role?: string
        displayName?: string
        assignManager?: boolean
        managerUserId?: string | null
        assignPartner?: boolean
        partnerUserId?: string | null
        clearPartner?: boolean
        skills?: string[]
      } = {}
      const u = users.find((x) => x.id === id)
      if (!u) return
      if (editEmail.trim().toLowerCase() !== u.email)
        body.email = editEmail.trim()
      if (editDisplayName.trim() !== u.displayName) body.displayName = editDisplayName.trim()
      if (editPassword.trim().length > 0) body.password = editPassword
      if (editRole !== u.role) body.role = editRole
      const origMgr = u.managerUserId ?? ''
      if (editManagerId !== origMgr) {
        body.assignManager = true
        body.managerUserId = editManagerId.trim().length ? editManagerId.trim() : null
      }
      const origPartner = u.partnerUserId ?? ''
      if (editPartnerId !== origPartner) {
        body.assignPartner = true
        if (!editPartnerId.trim()) {
          body.clearPartner = true
          body.partnerUserId = null
        } else body.partnerUserId = editPartnerId.trim()
      }
      if (editSkills.trim() !== u.skills.join(', ')) body.skills = parseSkillsCsv(editSkills)
      if (Object.keys(body).length === 0) {
        cancelEdit()
        return
      }
      const updated = await patchUser(token, id, body)
      const roleOnly =
        body.role !== undefined &&
        body.email === undefined &&
        body.password === undefined &&
        body.displayName === undefined &&
        body.assignManager !== true &&
        body.assignPartner !== true
      pushToast(roleOnly ? 'Role updated' : 'User updated', 'ok')
      cancelEdit()
      if (id === profile.id && updated.role !== 'Admin') onSignOut()
      else await refresh()
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Update failed', 'err')
    } finally {
      setBusyId(null)
    }
  }

  const requestDeactivate = (id: string) => setConfirmDeactivateId(id)

  const confirmDeactivate = async () => {
    if (!confirmDeactivateId) return
    const id = confirmDeactivateId
    setConfirmDeactivateId(null)
    setBusyId(id)
    try {
      await patchUser(token, id, { isActive: false })
      pushToast('User deactivated', 'ok')
      await refresh()
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Deactivate failed', 'err')
    } finally {
      setBusyId(null)
    }
  }

  const reactivate = async (id: string) => {
    setBusyId(id)
    try {
      await patchUser(token, id, { isActive: true })
      pushToast('User reactivated', 'ok')
      await refresh()
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Reactivate failed', 'err')
    } finally {
      setBusyId(null)
    }
  }

  const requestDelete = (id: string) => {
    if (id === profile.id) {
      pushToast('You cannot delete your own account from here.', 'err')
      return
    }
    setConfirmDeleteId(id)
  }

  const confirmDelete = async () => {
    if (!confirmDeleteId) return
    const id = confirmDeleteId
    setConfirmDeleteId(null)
    setBusyId(id)
    try {
      await deleteUser(token, id)
      pushToast('User and related data removed', 'ok')
      await refresh()
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Delete failed', 'err')
    } finally {
      setBusyId(null)
    }
  }

  return (
    <div className="admin-wrap">
      <div className="card admin-card">
        <h1 className="title admin-title">Users</h1>
        <p className="subtitle admin-sub">
          Signed in as {profile.displayName} ({profile.email}) · Admin
        </p>
      </div>

      <div className="card admin-card">
        <h2 className="admin-h2">Create User</h2>
        <p className="admin-hint">
          Choose a role for the new account. Password min. 8 characters. IC always needs an org manager (Manager
          role). Additional Manager accounts need one too when any Manager already exists. Finance gets a reporting
          partner automatically (first active Partner) if you leave the partner field blank. Managers need a partner
          when another Manager already exists (PTO and timesheet routing).
        </p>
        <form className="form admin-form-grid" onSubmit={onCreate}>
          <label className="field">
            <span>Display Name</span>
            <input
              type="text"
              value={createDisplayName}
              onChange={(e) => setCreateDisplayName(e.target.value)}
              placeholder="Optional; defaults to email before @"
              maxLength={80}
              autoComplete="off"
            />
          </label>
          <label className="field">
            <span>Email</span>
            <input
              type="email"
              value={createEmail}
              onChange={(e) => setCreateEmail(e.target.value)}
              required
              autoComplete="off"
            />
          </label>
          <label className="field">
            <span>Initial Password</span>
            <input
              type="password"
              value={createPassword}
              onChange={(e) => setCreatePassword(e.target.value)}
              required
              minLength={8}
              autoComplete="new-password"
            />
          </label>
          <label className="field">
            <span>Role</span>
            <select value={createRole} onChange={(e) => setCreateRole(e.target.value)} aria-label="Role for new user">
              {APP_ROLES.map((r) => (
                <option key={r} value={r}>
                  {r}
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            <span>Org manager {createNeedsOrgManager ? '(required)' : '(optional)'}</span>
            <select value={createManagerId} onChange={(e) => setCreateManagerId(e.target.value)}>
              <option value="">— {createNeedsOrgManager ? 'Select manager —' : 'None —'}</option>
              {users
                .filter((u) => u.role === 'Manager' && u.isActive)
                .map((m) => (
                  <option key={m.id} value={m.id}>
                    {m.email}
                  </option>
                ))}
            </select>
          </label>
          <label className="field">
            <span>Reporting partner {createNeedsReportingPartner ? '(required)' : '(optional)'}</span>
            <select value={createPartnerId} onChange={(e) => setCreatePartnerId(e.target.value)}>
              <option value="">— {createNeedsReportingPartner ? 'Select partner —' : 'None —'}</option>
              {activePartners.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.email}
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            <span>Skills (comma separated)</span>
            <input
              type="text"
              value={createSkills}
              onChange={(e) => setCreateSkills(e.target.value)}
              placeholder="c#, react, sql"
              autoComplete="off"
            />
          </label>
          <button type="submit" className="btn primary">
            Create
          </button>
        </form>
      </div>

      <div className="card admin-card">
        <div className="admin-table-head">
          <h2 className="admin-h2">All Users</h2>
          <button type="button" className="btn secondary btn-sm" onClick={() => void refresh()} disabled={loading}>
            Refresh
          </button>
        </div>
        {loading ? (
          <p className="admin-hint">Loading…</p>
        ) : (
          <div className="table-scroll">
            <table className="admin-table">
              <thead>
                <tr>
                  <th>Email</th>
                  <th>Display Name</th>
                  <th>Role</th>
                  <th>Manager</th>
                  <th>Partner</th>
                  <th>Skills</th>
                  <th>Status</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {users.map((u) => (
                  <tr key={u.id}>
                    <td>
                      {editingId === u.id ? (
                        <input
                          className="table-input"
                          value={editEmail}
                          onChange={(e) => setEditEmail(e.target.value)}
                          aria-label="Edit email"
                        />
                      ) : (
                        u.email
                      )}
                    </td>
                    <td>
                      {editingId === u.id ? (
                        <input
                          className="table-input"
                          value={editDisplayName}
                          onChange={(e) => setEditDisplayName(e.target.value)}
                          maxLength={80}
                          aria-label="Edit display name"
                        />
                      ) : (
                        u.displayName
                      )}
                    </td>
                    <td>
                      {editingId === u.id ? (
                        <select
                          className="table-input role-select"
                          value={editRole}
                          onChange={(e) => setEditRole(e.target.value)}
                          aria-label="Role"
                        >
                          {APP_ROLES.map((r) => (
                            <option key={r} value={r}>
                              {r}
                            </option>
                          ))}
                        </select>
                      ) : (
                        <span className="role-pill">{u.role}</span>
                      )}
                    </td>
                    <td>
                      {editingId === u.id ? (
                        <select
                          className="table-input"
                          value={editManagerId}
                          onChange={(e) => setEditManagerId(e.target.value)}
                          aria-label="Manager"
                        >
                          <option value="">— None —</option>
                          {users
                            .filter((x) => x.role === 'Manager' && x.isActive && x.id !== u.id)
                            .map((m) => (
                              <option key={m.id} value={m.id}>
                                {m.email}
                              </option>
                            ))}
                        </select>
                      ) : u.managerUserId ? (
                        users.find((x) => x.id === u.managerUserId)?.email ?? '—'
                      ) : (
                        '—'
                      )}
                    </td>
                    <td>
                      {editingId === u.id ? (
                        <select
                          className="table-input"
                          value={editPartnerId}
                          onChange={(e) => setEditPartnerId(e.target.value)}
                          aria-label="Reporting partner"
                        >
                          <option value="">— None —</option>
                          {activePartners.map((p) => (
                            <option key={p.id} value={p.id}>
                              {p.email}
                            </option>
                          ))}
                        </select>
                      ) : u.partnerUserId ? (
                        users.find((x) => x.id === u.partnerUserId)?.email ?? '—'
                      ) : (
                        '—'
                      )}
                    </td>
                    <td>
                      {editingId === u.id ? (
                        <input
                          className="table-input"
                          value={editSkills}
                          onChange={(e) => setEditSkills(e.target.value)}
                          placeholder="c#, react, sql"
                          aria-label="Skills"
                        />
                      ) : u.skills.length > 0 ? (
                        u.skills.join(', ')
                      ) : (
                        '—'
                      )}
                    </td>
                    <td>
                      {u.isActive ? (
                        <span className="status-active">Active</span>
                      ) : (
                        <span className="status-inactive">Inactive</span>
                      )}
                    </td>
                    <td className="admin-actions">
                      {editingId === u.id ? (
                        <>
                          <label className="field inline-field">
                            <span className="sr-only">New Password (Optional)</span>
                            <input
                              type="password"
                              placeholder="New Password (Optional)"
                              value={editPassword}
                              onChange={(e) => setEditPassword(e.target.value)}
                              minLength={8}
                              autoComplete="new-password"
                            />
                          </label>
                          <button
                            type="button"
                            className="btn primary btn-sm"
                            disabled={busyId === u.id}
                            onClick={() => void saveEdit(u.id)}
                          >
                            Save
                          </button>
                          <button type="button" className="btn secondary btn-sm" onClick={cancelEdit}>
                            Cancel
                          </button>
                        </>
                      ) : (
                        <>
                          <button type="button" className="btn secondary btn-sm" onClick={() => startEdit(u)}>
                            Edit
                          </button>
                          {u.isActive ? (
                            confirmDeactivateId === u.id ? (
                              <span className="inline-confirm">
                                <span>Deactivate?</span>
                                <button
                                  type="button"
                                  className="btn primary btn-sm"
                                  disabled={busyId === u.id}
                                  onClick={() => void confirmDeactivate()}
                                >
                                  Yes
                                </button>
                                <button
                                  type="button"
                                  className="btn secondary btn-sm"
                                  onClick={() => setConfirmDeactivateId(null)}
                                >
                                  No
                                </button>
                              </span>
                            ) : (
                              <button
                                type="button"
                                className="btn secondary btn-sm danger-outline"
                                disabled={busyId === u.id}
                                onClick={() => requestDeactivate(u.id)}
                              >
                                Deactivate
                              </button>
                            )
                          ) : (
                            <button
                              type="button"
                              className="btn secondary btn-sm"
                              disabled={busyId === u.id}
                              onClick={() => void reactivate(u.id)}
                            >
                              Reactivate
                            </button>
                          )}
                          {u.id !== profile.id &&
                            (confirmDeleteId === u.id ? (
                              <span className="inline-confirm">
                                <span>Delete user and their data?</span>
                                <button
                                  type="button"
                                  className="btn primary btn-sm"
                                  disabled={busyId === u.id}
                                  onClick={() => void confirmDelete()}
                                >
                                  Yes
                                </button>
                                <button
                                  type="button"
                                  className="btn secondary btn-sm"
                                  onClick={() => setConfirmDeleteId(null)}
                                >
                                  No
                                </button>
                              </span>
                            ) : (
                              <button
                                type="button"
                                className="btn secondary btn-sm danger-outline"
                                disabled={busyId === u.id}
                                onClick={() => requestDelete(u.id)}
                              >
                                Delete
                              </button>
                            ))}
                        </>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

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

function parseSkillsCsv(raw: string): string[] {
  return raw
    .split(',')
    .map((x) => x.trim().toLowerCase())
    .filter((x) => x.length > 0)
    .filter((x, i, arr) => arr.indexOf(x) === i)
}
