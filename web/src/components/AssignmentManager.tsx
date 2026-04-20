import { useCallback, useEffect, useMemo, useState } from 'react'
import type { AssignmentRow } from '../api'

type TargetOption = { id: string; name: string; isActive?: boolean }

export default function AssignmentManager({
  token,
  title,
  targetLabel,
  targetOptions,
  loadAssignableEmployees,
  loadAssignments,
  assign,
  unassign,
  canManage,
}: {
  token: string
  title: string
  targetLabel: string
  targetOptions: TargetOption[]
  loadAssignableEmployees: (token: string) => Promise<AssignmentRow[]>
  loadAssignments: (token: string, targetId: string) => Promise<AssignmentRow[]>
  assign: (token: string, targetId: string, userId: string) => Promise<void>
  unassign: (token: string, targetId: string, userId: string) => Promise<void>
  canManage: boolean
}) {
  const activeTargets = useMemo(
    () => targetOptions.filter((t) => t.isActive !== false),
    [targetOptions],
  )
  const [selectedTargetId, setSelectedTargetId] = useState('')
  const [employees, setEmployees] = useState<AssignmentRow[]>([])
  const [assigned, setAssigned] = useState<AssignmentRow[]>([])
  const [selectedUserId, setSelectedUserId] = useState('')
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (activeTargets.length === 0) {
      setSelectedTargetId('')
      return
    }
    setSelectedTargetId((prev) =>
      prev && activeTargets.some((t) => t.id === prev) ? prev : activeTargets[0]!.id,
    )
  }, [activeTargets])

  const refresh = useCallback(async () => {
    if (!canManage || !selectedTargetId) {
      setEmployees([])
      setAssigned([])
      setLoading(false)
      return
    }
    setLoading(true)
    setError(null)
    try {
      const [employeeRows, assignedRows] = await Promise.all([
        loadAssignableEmployees(token),
        loadAssignments(token, selectedTargetId),
      ])
      setEmployees(employeeRows)
      setAssigned(assignedRows)
      setSelectedUserId((prev) => {
        if (prev && employeeRows.some((e) => e.userId === prev)) return prev
        const first = employeeRows.find((e) => !assignedRows.some((a) => a.userId === e.userId))
        return first?.userId ?? ''
      })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load assignments')
    } finally {
      setLoading(false)
    }
  }, [canManage, loadAssignableEmployees, loadAssignments, selectedTargetId, token])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const assignableEmployees = useMemo(
    () => employees.filter((e) => !assigned.some((a) => a.userId === e.userId)),
    [assigned, employees],
  )

  useEffect(() => {
    if (assignableEmployees.length === 0) {
      setSelectedUserId('')
      return
    }
    setSelectedUserId((prev) =>
      prev && assignableEmployees.some((e) => e.userId === prev) ? prev : assignableEmployees[0]!.userId,
    )
  }, [assignableEmployees])

  const onAssign = async () => {
    if (!selectedTargetId || !selectedUserId) return
    setBusy(true)
    setError(null)
    try {
      await assign(token, selectedTargetId, selectedUserId)
      await refresh()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Assign failed')
    } finally {
      setBusy(false)
    }
  }

  const onUnassign = async (userId: string) => {
    if (!selectedTargetId) return
    setBusy(true)
    setError(null)
    try {
      await unassign(token, selectedTargetId, userId)
      await refresh()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unassign failed')
    } finally {
      setBusy(false)
    }
  }

  if (!canManage) return null

  return (
    <div className="card admin-card">
      <div className="admin-table-head">
        <h2 className="admin-h2">{title}</h2>
        <button type="button" className="btn secondary btn-sm" onClick={() => void refresh()} disabled={loading || busy}>
          Refresh
        </button>
      </div>

      <div className="assignment-controls">
        <label className="field">
          <span>{targetLabel}</span>
          <select value={selectedTargetId} onChange={(e) => setSelectedTargetId(e.target.value)}>
            {activeTargets.map((t) => (
              <option key={t.id} value={t.id}>
                {t.name}
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          <span>Employee</span>
          <select
            value={selectedUserId}
            onChange={(e) => setSelectedUserId(e.target.value)}
            disabled={assignableEmployees.length === 0}
          >
            {assignableEmployees.length === 0 ? (
              <option value="">No unassigned employees</option>
            ) : (
              assignableEmployees.map((e) => (
                <option key={e.userId} value={e.userId}>
                  {e.displayName} ({e.role})
                </option>
              ))
            )}
          </select>
        </label>
        <button
          type="button"
          className="btn primary btn-sm"
          onClick={() => void onAssign()}
          disabled={busy || loading || !selectedTargetId || !selectedUserId}
        >
          Assign
        </button>
      </div>

      {error ? <p className="err">{error}</p> : null}
      {loading ? (
        <p className="admin-hint">Loading assignments…</p>
      ) : assigned.length === 0 ? (
        <p className="admin-hint">No employees assigned.</p>
      ) : (
        <ul className="assignment-list">
          {assigned.map((a) => (
            <li key={a.userId} className="assignment-item">
              <span>
                <strong>{a.displayName}</strong> · {a.email} · {a.role}
              </span>
              <button
                type="button"
                className="btn secondary btn-sm"
                onClick={() => void onUnassign(a.userId)}
                disabled={busy}
              >
                Remove
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
