import { useCallback, useEffect, useState } from 'react'
import { createClient, listClients, patchClient, type ClientRow, type MeProfile } from '../api'
import '../App.css'

type Toast = { id: number; message: string; variant: 'ok' | 'err' }

const TOAST_MS = 4000
const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })

export default function ClientsPage({
  token,
  profile,
}: {
  token: string
  profile: MeProfile
}) {
  const isAdmin = profile.role === 'Admin'
  const canSeeRates = profile.role === 'Admin' || profile.role === 'Finance'
  const canCreateClient =
    profile.role === 'Admin' || profile.role === 'Partner' || profile.role === 'Finance'
  const canEditClient = isAdmin

  const [rows, setRows] = useState<ClientRow[]>([])
  const [loading, setLoading] = useState(true)
  const [q, setQ] = useState('')
  const [includeInactive, setIncludeInactive] = useState(false)
  const [toasts, setToasts] = useState<Toast[]>([])

  const [createName, setCreateName] = useState('')
  const [createEmail, setCreateEmail] = useState('')
  const [createRate, setCreateRate] = useState('')
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editName, setEditName] = useState('')
  const [editActive, setEditActive] = useState(true)

  const pushToast = useCallback((message: string, variant: 'ok' | 'err') => {
    const id = Date.now()
    setToasts((t) => [...t, { id, message, variant }])
    window.setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), TOAST_MS)
  }, [])

  const refresh = useCallback(async () => {
    setLoading(true)
    try {
      const list = await listClients(token, q || undefined, isAdmin && includeInactive)
      setRows(list)
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Load failed', 'err')
    } finally {
      setLoading(false)
    }
  }, [token, q, includeInactive, isAdmin, pushToast])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const onCreate = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!canCreateClient) return
    try {
      const rateNum = createRate.trim() === '' ? undefined : Number(createRate)
      await createClient(token, {
        name: createName.trim(),
        contactEmail: createEmail.trim() || undefined,
        defaultBillingRate: canSeeRates && Number.isFinite(rateNum) ? rateNum : undefined,
      })
      setCreateName('')
      setCreateEmail('')
      setCreateRate('')
      pushToast('Client created', 'ok')
      await refresh()
    } catch (err) {
      pushToast(err instanceof Error ? err.message : 'Create failed', 'err')
    }
  }

  const startEdit = (c: ClientRow) => {
    setEditingId(c.id)
    setEditName(c.name)
    setEditActive(c.isActive)
  }

  const saveEdit = async (id: string) => {
    if (!canEditClient) return
    try {
      await patchClient(token, id, { name: editName.trim(), isActive: editActive })
      setEditingId(null)
      pushToast('Client updated', 'ok')
      await refresh()
    } catch (err) {
      pushToast(err instanceof Error ? err.message : 'Update failed', 'err')
    }
  }

  return (
    <div className="admin-wrap">
      <div className="card admin-card">
        <h1 className="title admin-title">Clients</h1>
        <p className="subtitle admin-sub">
          Signed in as {profile.email} · {profile.role}
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
            <span>Search name</span>
            <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Contains…" />
          </label>
          {isAdmin ? (
            <label className="field" style={{ flexDirection: 'row', alignItems: 'center', gap: 8 }}>
              <input
                type="checkbox"
                checked={includeInactive}
                onChange={(e) => setIncludeInactive(e.target.checked)}
              />
              <span>Show inactive (admin)</span>
            </label>
          ) : null}
        </div>
      </div>

      {canCreateClient ? (
        <div className="card admin-card">
          <h2 className="admin-h2">New client</h2>
          <p className="admin-hint">
            {canSeeRates
              ? 'Billing rate is visible only to Admin and Finance (per PRD).'
              : 'Partner creates the shell client; Admin or Finance can set billing rate when ready.'}
          </p>
          <form className="form admin-form-grid" onSubmit={onCreate}>
            <label className="field">
              <span>Name</span>
              <input value={createName} onChange={(e) => setCreateName(e.target.value)} required />
            </label>
            <label className="field">
              <span>Contact email</span>
              <input type="email" value={createEmail} onChange={(e) => setCreateEmail(e.target.value)} />
            </label>
            {canSeeRates ? (
              <label className="field">
                <span>Default $/hr</span>
                <input inputMode="decimal" value={createRate} onChange={(e) => setCreateRate(e.target.value)} />
              </label>
            ) : null}
            <button type="submit" className="btn primary">
              Create
            </button>
          </form>
        </div>
      ) : null}

      <div className="card admin-card">
        {loading ? (
          <p className="admin-hint">Loading…</p>
        ) : rows.length === 0 ? (
          <p className="admin-hint">No clients match.</p>
        ) : (
          <div className="table-scroll">
            <table className="admin-table">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Contact</th>
                  {canSeeRates ? <th>$/hr</th> : null}
                  <th>Status</th>
                  {canEditClient ? <th /> : null}
                </tr>
              </thead>
              <tbody>
                {rows.map((c) => (
                  <tr key={c.id}>
                    <td>
                      {editingId === c.id ? (
                        <input
                          className="table-input"
                          value={editName}
                          onChange={(e) => setEditName(e.target.value)}
                        />
                      ) : (
                        c.name
                      )}
                    </td>
                    <td>{c.contactEmail ?? '—'}</td>
                    {canSeeRates ? <td>{c.defaultBillingRate != null ? usd.format(c.defaultBillingRate) : '—'}</td> : null}
                    <td>
                      {editingId === c.id ? (
                        <label style={{ display: 'flex', alignItems: 'center', gap: 6, whiteSpace: 'nowrap' }}>
                          <input type="checkbox" checked={editActive} onChange={(e) => setEditActive(e.target.checked)} />
                          active
                        </label>
                      ) : c.isActive ? (
                        'Active'
                      ) : (
                        'Inactive'
                      )}
                    </td>
                    {canEditClient ? (
                      <td>
                        {editingId === c.id ? (
                          <>
                            <button type="button" className="btn primary btn-sm" onClick={() => void saveEdit(c.id)}>
                              Save
                            </button>{' '}
                            <button type="button" className="btn secondary btn-sm" onClick={() => setEditingId(null)}>
                              Cancel
                            </button>
                          </>
                        ) : (
                          <button type="button" className="btn secondary btn-sm" onClick={() => startEdit(c)}>
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
