import { useCallback, useEffect, useState } from 'react'
import { useOutletContext } from 'react-router-dom'
import {
  createProjectTask,
  deleteProjectTask,
  listProjectStaffingUsers,
  listProjectTasks,
  listProjects,
  patchProjectTask,
  recommendProjectTask,
  type ProjectRow,
  type ProjectStaffingUserRow,
  type ProjectTaskRow,
  type StaffingRecommendationResponse,
} from '../api'
import type { ResourceTrackerLayoutSession } from './ResourceTrackerLayout'
import '../App.css'

type Toast = { id: number; message: string; variant: 'ok' | 'err' }
const TOAST_MS = 4000

const STATUS_OPTIONS = ['Open', 'InProgress', 'Done', 'Cancelled'] as const

function skillsToInput(skills: string[]) {
  return skills.join(', ')
}

function parseSkillsInput(raw: string) {
  return raw
    .split(',')
    .map((s) => s.trim())
    .filter((s) => s.length > 0)
}

function statusPillClass(status: string) {
  switch (status) {
    case 'InProgress':
      return 'is-inprogress'
    case 'Done':
      return 'is-done'
    case 'Cancelled':
      return 'is-cancelled'
    default:
      return 'is-open'
  }
}

export default function ResourceTrackerProjectTasks() {
  const session = useOutletContext<ResourceTrackerLayoutSession | null>()
  const [tasks, setTasks] = useState<ProjectTaskRow[]>([])
  const [projects, setProjects] = useState<ProjectRow[]>([])
  const [staff, setStaff] = useState<ProjectStaffingUserRow[]>([])
  const [loading, setLoading] = useState(true)
  const [toasts, setToasts] = useState<Toast[]>([])

  const [newProjectId, setNewProjectId] = useState('')
  const [newTitle, setNewTitle] = useState('')
  const [newSkills, setNewSkills] = useState('')
  const [newDue, setNewDue] = useState('')
  const [creating, setCreating] = useState(false)

  const [recoOpen, setRecoOpen] = useState(false)
  const [recoLoading, setRecoLoading] = useState(false)
  const [recoTaskId, setRecoTaskId] = useState<string | null>(null)
  const [recoBody, setRecoBody] = useState<StaffingRecommendationResponse | null>(null)

  const pushToast = useCallback((message: string, variant: 'ok' | 'err') => {
    const id = Date.now()
    setToasts((t) => [...t, { id, message, variant }])
    window.setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), TOAST_MS)
  }, [])

  const loadAll = useCallback(async () => {
    if (!session) return
    setLoading(true)
    try {
      const [t, p, s] = await Promise.all([
        listProjectTasks(session.token),
        listProjects(session.token, { includeInactive: false }),
        listProjectStaffingUsers(session.token),
      ])
      setTasks(t)
      setProjects(p.filter((x) => x.isActive))
      setStaff(s)
      setNewProjectId((prev) => {
        if (prev && p.some((x) => x.id === prev)) return prev
        const first = p.find((x) => x.isActive)
        return first?.id ?? ''
      })
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Load failed', 'err')
      setTasks([])
    } finally {
      setLoading(false)
    }
  }, [session, pushToast])

  useEffect(() => {
    void loadAll()
  }, [loadAll])

  const assigneeOptions = staff

  const onCreate = async () => {
    if (!session || !newProjectId || !newTitle.trim()) {
      pushToast('Pick a project and enter a task title', 'err')
      return
    }
    setCreating(true)
    try {
      await createProjectTask(session.token, {
        projectId: newProjectId,
        title: newTitle.trim(),
        requiredSkills: parseSkillsInput(newSkills),
        dueDate: newDue.trim() === '' ? null : newDue.trim(),
      })
      pushToast('Task created', 'ok')
      setNewTitle('')
      setNewSkills('')
      setNewDue('')
      await loadAll()
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Create failed', 'err')
    } finally {
      setCreating(false)
    }
  }

  const onSaveRow = async (row: ProjectTaskRow) => {
    if (!session) return
    try {
      const updated = await patchProjectTask(session.token, row.id, {
        title: row.title,
        description: row.description,
        requiredSkills: row.requiredSkills,
        dueDate: row.dueDate,
        status: row.status,
        ...(row.assignedUserId ? { assignedUserId: row.assignedUserId } : { clearAssignedUser: true }),
      })
      setTasks((prev) => prev.map((r) => (r.id === updated.id ? updated : r)))
      pushToast('Saved', 'ok')
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Save failed', 'err')
    }
  }

  const onDeleteRow = async (id: string) => {
    if (!session || !window.confirm('Delete this task?')) return
    try {
      await deleteProjectTask(session.token, id)
      pushToast('Deleted', 'ok')
      await loadAll()
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Delete failed', 'err')
    }
  }

  const patchLocal = (id: string, patch: Partial<ProjectTaskRow>) => {
    setTasks((prev) => prev.map((r) => (r.id === id ? { ...r, ...patch } : r)))
  }

  const openRecommend = async (taskId: string) => {
    if (!session) return
    setRecoTaskId(taskId)
    setRecoOpen(true)
    setRecoLoading(true)
    setRecoBody(null)
    try {
      const body = await recommendProjectTask(session.token, taskId)
      setRecoBody(body)
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Recommendations failed', 'err')
      setRecoOpen(false)
    } finally {
      setRecoLoading(false)
    }
  }

  const applyTopCandidate = async () => {
    if (!session || !recoTaskId || !recoBody?.results?.length) return
    const top = recoBody.results[0]!
    try {
      const updated = await patchProjectTask(session.token, recoTaskId, { assignedUserId: top.userId })
      setTasks((prev) => prev.map((r) => (r.id === updated.id ? updated : r)))
      pushToast(`Assigned ${top.displayName || top.email}`, 'ok')
      setRecoOpen(false)
    } catch (e) {
      pushToast(e instanceof Error ? e.message : 'Assign failed', 'err')
    }
  }

  if (!session) {
    return (
      <div className="admin-wrap">
        <p className="admin-hint">Sign in to use the project task board.</p>
      </div>
    )
  }

  const { profile } = session
  const canUseBoard =
    profile.role === 'Admin' || profile.role === 'Manager' || profile.role === 'Partner'

  if (!canUseBoard) {
    return (
      <div className="admin-wrap">
        <div className="card admin-card">
          <h1 className="title admin-title">Project task board</h1>
          <p className="admin-hint">This view is available to Admin, Manager, and Partner roles.</p>
        </div>
      </div>
    )
  }

  return (
    <div className="admin-wrap">
      <div className="card admin-card resource-page-header-card">
        <p className="resource-page-eyebrow">Resource Tracker</p>
        <h1 className="title admin-title">Project task board</h1>
        <p className="subtitle admin-sub">
          Signed in as {profile.displayName} · {profile.role}
        </p>
        <p className="admin-hint" style={{ marginTop: 8 }}>
          Excel-style grid for <strong>active projects</strong>: define work items, required skills, dates, and owners.
          Use <strong>Recommend</strong> to score people against this task&apos;s project and skills (same engine as
          project staffing recommendations). Managers and partners can edit; Finance and IC use the People tab for
          utilization context.
        </p>
      </div>

      <div className="card admin-card resource-section-card">
        <div className="admin-table-head resource-section-head">
          <h2 className="admin-h2 resource-section-title">Add Task</h2>
        </div>
        <div className="form admin-form-grid resource-task-add-grid" style={{ maxWidth: 'none', marginTop: 0 }}>
          <label className="field">
            <span>Project</span>
            <select value={newProjectId} onChange={(e) => setNewProjectId(e.target.value)}>
              {projects.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.clientName} — {p.name}
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            <span>Task title</span>
            <input value={newTitle} onChange={(e) => setNewTitle(e.target.value)} placeholder="e.g. Cutover support" />
          </label>
          <label className="field">
            <span>Required skills (comma)</span>
            <input
              value={newSkills}
              onChange={(e) => setNewSkills(e.target.value)}
              placeholder="c#, sql, react"
            />
          </label>
          <label className="field">
            <span>Due date</span>
            <input type="date" value={newDue} onChange={(e) => setNewDue(e.target.value)} />
          </label>
          <div style={{ display: 'flex', alignItems: 'flex-end' }}>
            <button type="button" className="btn primary btn-sm" disabled={creating} onClick={() => void onCreate()}>
              {creating ? 'Creating…' : 'Create task'}
            </button>
          </div>
        </div>
      </div>

      <div className="card admin-card resource-section-card">
        <div className="admin-table-head resource-section-head">
          <h2 className="admin-h2 resource-section-title">Tasks</h2>
          <button type="button" className="btn secondary btn-sm" onClick={() => void loadAll()} disabled={loading}>
            Refresh
          </button>
        </div>
        {loading ? (
          <p className="admin-hint">Loading…</p>
        ) : (
          <div className="table-scroll">
            <table className="resource-matrix project-task-board">
              <thead>
                <tr>
                  <th className="sticky-col">Client · Project</th>
                  <th>Task</th>
                  <th>Description</th>
                  <th>Skills (comma)</th>
                  <th>Due</th>
                  <th>Status</th>
                  <th>Assigned</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {tasks.length === 0 ? (
                  <tr>
                    <td colSpan={8} className="admin-hint">
                      No tasks yet — create one above.
                    </td>
                  </tr>
                ) : (
                  tasks.map((row) => (
                    <tr key={row.id}>
                      <td className="sticky-col" title={`${row.clientName} — ${row.projectName}`}>
                        <strong>{row.clientName}</strong>
                        <br />
                        <span className="admin-hint">{row.projectName}</span>
                      </td>
                      <td>
                        <input
                          className="table-input"
                          value={row.title}
                          onChange={(e) => patchLocal(row.id, { title: e.target.value })}
                        />
                      </td>
                      <td>
                        <input
                          className="table-input"
                          value={row.description ?? ''}
                          onChange={(e) => patchLocal(row.id, { description: e.target.value || null })}
                        />
                      </td>
                      <td>
                        <input
                          className="table-input"
                          value={skillsToInput(row.requiredSkills)}
                          onChange={(e) =>
                            patchLocal(row.id, { requiredSkills: parseSkillsInput(e.target.value) })
                          }
                        />
                      </td>
                      <td>
                        <input
                          type="date"
                          className="table-input"
                          value={row.dueDate ?? ''}
                          onChange={(e) => patchLocal(row.id, { dueDate: e.target.value || null })}
                        />
                      </td>
                      <td>
                        <select
                          className={`resource-task-status-select ${statusPillClass(row.status)}`}
                          value={row.status}
                          onChange={(e) => patchLocal(row.id, { status: e.target.value })}
                        >
                          {STATUS_OPTIONS.map((s) => (
                            <option key={s} value={s}>
                              {s}
                            </option>
                          ))}
                        </select>
                      </td>
                      <td>
                        <select
                          className="resource-task-assignee-select"
                          value={row.assignedUserId ?? ''}
                          onChange={(e) =>
                            patchLocal(row.id, {
                              assignedUserId: e.target.value === '' ? null : e.target.value,
                              assignedEmail:
                                assigneeOptions.find((u) => u.id === e.target.value)?.email ?? null,
                            })
                          }
                        >
                          <option value="">— Unassigned</option>
                          {assigneeOptions.map((u) => (
                            <option key={u.id} value={u.id}>
                              {u.displayName || u.email} ({u.role})
                            </option>
                          ))}
                        </select>
                      </td>
                      <td>
                        <div className="resource-task-actions">
                          <button
                            type="button"
                            className="btn primary btn-sm"
                            onClick={() => void onSaveRow(row)}
                          >
                            Save
                          </button>
                          <button
                            type="button"
                            className="btn secondary btn-sm"
                            onClick={() => void openRecommend(row.id)}
                          >
                            Recommend
                          </button>
                          <button
                            type="button"
                            className="btn secondary btn-sm"
                            onClick={() => void onDeleteRow(row.id)}
                          >
                            Delete
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {recoOpen ? (
        <div className="resource-tracker-reco-overlay" role="dialog" aria-modal="true" aria-label="Recommendations">
          <div className="resource-tracker-reco-dialog card admin-card">
            <div className="admin-table-head">
              <h2 className="admin-h2">Staffing recommendations</h2>
              <button type="button" className="btn secondary btn-sm" onClick={() => setRecoOpen(false)}>
                Close
              </button>
            </div>
            {recoLoading ? <p className="admin-hint">Scoring candidates…</p> : null}
            {recoBody?.warningMessage ? <p className="admin-hint">{recoBody.warningMessage}</p> : null}
            {!recoLoading && recoBody ? (
              <>
                <p className="admin-hint">
                  Mode: <strong>{recoBody.fallbackMode}</strong>
                </p>
                <ul className="recommendation-list">
                  {recoBody.results.slice(0, 8).map((r) => (
                    <li key={r.userId} className="recommendation-item">
                      <div>
                        <strong>{r.displayName}</strong> ({r.role}) · total {r.totalScore.toFixed(2)} · skill{' '}
                        {r.skillScore.toFixed(2)}
                      </div>
                      <div className="recommendation-meta">{r.rationale.join(' ')}</div>
                    </li>
                  ))}
                </ul>
                {recoBody.results.length > 0 ? (
                  <button type="button" className="btn primary btn-sm" onClick={() => void applyTopCandidate()}>
                    Assign top candidate
                  </button>
                ) : (
                  <p className="admin-hint">No candidates returned — widen skills or check project roster.</p>
                )}
              </>
            ) : null}
          </div>
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
