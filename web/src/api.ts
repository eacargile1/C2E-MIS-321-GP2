const rawBase = import.meta.env.VITE_API_BASE_URL
const base =
  typeof rawBase === 'string' && rawBase.trim().length > 0
    ? rawBase.trim().replace(/\/$/, '')
    : 'http://localhost:5028'

async function parseJsonResponse(res: Response, parseErrorMessage: string): Promise<unknown> {
  const text = await res.text()
  if (!text.trim()) throw new Error(parseErrorMessage)
  try {
    return JSON.parse(text) as unknown
  } catch {
    throw new Error(parseErrorMessage)
  }
}

/** Parses AuthErrorResponse, ProblemDetails, or similar JSON from the API. */
function messageFromApiJson(data: unknown, status: number, fallback: string): string {
  if (!data || typeof data !== 'object') return `${fallback} (HTTP ${status})`
  const r = data as Record<string, unknown>
  if (typeof r.message === 'string' && r.message.trim()) return r.message
  if (typeof r.title === 'string' && r.title.trim()) return r.title
  const errs = r.errors
  if (errs && typeof errs === 'object' && errs !== null) {
    for (const v of Object.values(errs)) {
      if (Array.isArray(v)) {
        const first = v.find((x) => typeof x === 'string')
        if (typeof first === 'string' && first.trim()) return first
      }
    }
  }
  return `${fallback} (HTTP ${status})`
}

export async function readApiErrorMessage(res: Response, fallback: string): Promise<string> {
  try {
    const text = await res.text()
    if (!text.trim()) return `${fallback} (HTTP ${res.status})`
    try {
      const data = JSON.parse(text) as unknown
      return messageFromApiJson(data, res.status, fallback)
    } catch {
      return `${fallback} (HTTP ${res.status})`
    }
  } catch {
    return `${fallback} (HTTP ${res.status})`
  }
}

export type MeProfile = {
  id: string
  email: string
  displayName: string
  role: string
  isActive: boolean
}

export type UserRow = {
  id: string
  email: string
  displayName: string
  role: string
  isActive: boolean
  managerUserId: string | null
  partnerUserId: string | null
  skills: string[]
}

function emailLocalPart(email: string) {
  const i = email.indexOf('@')
  return i > 0 ? email.slice(0, i) : email
}

export async function login(email: string, password: string) {
  const res = await fetch(`${base}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
  })
  const text = await res.text()
  let data: unknown
  try {
    data = text.trim() ? JSON.parse(text) : null
  } catch {
    throw new Error(`Sign-in failed — API returned non-JSON (HTTP ${res.status}). Is ${base} the running API?`)
  }
  if (!res.ok) {
    throw new Error(messageFromApiJson(data, res.status, 'Sign-in failed'))
  }
  const ok = data as Record<string, unknown>
  if (typeof ok.accessToken !== 'string') throw new Error('Sign-in failed — missing token in response.')
  return ok as { accessToken: string; tokenType: string; expiresInSeconds: number }
}

export async function me(token: string): Promise<MeProfile> {
  const res = await fetch(`${base}/api/auth/me`, {
    headers: { Authorization: `Bearer ${token}` },
  })
  if (!res.ok) throw new Error('Session invalid')
  const data = (await parseJsonResponse(res, 'Session invalid')) as Record<string, unknown>
  if (
    typeof data.id !== 'string' ||
    typeof data.email !== 'string' ||
    typeof data.role !== 'string' ||
    typeof data.isActive !== 'boolean'
  )
    throw new Error('Session invalid')
  const displayName =
    typeof data.displayName === 'string' && data.displayName.trim().length > 0
      ? data.displayName.trim()
      : emailLocalPart(data.email)
  return {
    id: data.id,
    email: data.email,
    displayName,
    role: data.role,
    isActive: data.isActive,
  }
}

function authHeaders(token: string) {
  return {
    Authorization: `Bearer ${token}`,
    'Content-Type': 'application/json',
  } as const
}

function parseUserRow(r: Record<string, unknown>): UserRow {
  if (
    typeof r.id !== 'string' ||
    typeof r.email !== 'string' ||
    typeof r.role !== 'string' ||
    typeof r.isActive !== 'boolean'
  )
    throw new Error('Could not load users')
  const displayName =
    typeof r.displayName === 'string' && r.displayName.trim().length > 0
      ? r.displayName.trim()
      : emailLocalPart(r.email)
  const mid = r.managerUserId
  const managerUserId =
    typeof mid === 'string' ? mid : mid === null || typeof mid === 'undefined' ? null : String(mid)
  const pid = r.partnerUserId
  const partnerUserId =
    typeof pid === 'string' ? pid : pid === null || typeof pid === 'undefined' ? null : String(pid)
  const rawSkills = Array.isArray(r.skills) ? r.skills : []
  const skills = rawSkills.filter((x): x is string => typeof x === 'string')
  return {
    id: r.id,
    email: r.email,
    displayName,
    role: r.role,
    isActive: r.isActive,
    managerUserId,
    partnerUserId,
    skills,
  }
}

export async function listUsers(token: string): Promise<UserRow[]> {
  const res = await fetch(`${base}/api/users`, { headers: { Authorization: `Bearer ${token}` } })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load users'))
  const data = (await res.json()) as unknown
  if (!Array.isArray(data)) throw new Error('Could not load users')
  return data.map((row) => parseUserRow(row as Record<string, unknown>))
}

export async function createUser(
  token: string,
  email: string,
  password: string,
  opts?: {
    displayName?: string
    managerUserId?: string | null
    partnerUserId?: string | null
    role?: string
    skills?: string[]
  },
): Promise<UserRow> {
  const body: Record<string, unknown> = { email, password }
  if (opts?.displayName !== undefined && opts.displayName.trim() !== '')
    body.displayName = opts.displayName.trim()
  if (opts?.managerUserId !== undefined) body.managerUserId = opts.managerUserId
  if (opts?.partnerUserId !== undefined) body.partnerUserId = opts.partnerUserId
  if (opts?.role !== undefined && opts.role.trim() !== '') body.role = opts.role.trim()
  if (opts?.skills !== undefined) body.skills = opts.skills
  const res = await fetch(`${base}/api/users`, {
    method: 'POST',
    headers: authHeaders(token),
    body: JSON.stringify(body),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not create user'))
  const r = (await res.json()) as Record<string, unknown>
  return parseUserRow(r)
}

export async function patchUser(
  token: string,
  id: string,
  body: {
    email?: string
    password?: string
    isActive?: boolean
    role?: string
    displayName?: string
    assignManager?: boolean
    managerUserId?: string | null
    assignPartner?: boolean
    partnerUserId?: string | null
    clearPartner?: boolean
    skills?: string[]
  },
): Promise<UserRow> {
  const res = await fetch(`${base}/api/users/${id}`, {
    method: 'PATCH',
    headers: authHeaders(token),
    body: JSON.stringify(body),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not update user'))
  const r = (await res.json()) as Record<string, unknown>
  return parseUserRow(r)
}

export async function deleteUser(token: string, id: string): Promise<void> {
  const res = await fetch(`${base}/api/users/${encodeURIComponent(id)}`, {
    method: 'DELETE',
    headers: authHeaders(token),
  })
  if (!res.ok && res.status !== 404)
    throw new Error(await readApiErrorMessage(res, 'Could not delete user'))
}

export type PtoRequestRow = {
  id: string
  userId: string
  userEmail: string
  startDate: string
  endDate: string
  reason: string
  status: string
  approverUserId: string
  approverEmail: string
  secondaryApproverUserId?: string | null
  secondaryApproverEmail?: string | null
  createdAtUtc: string
  reviewedAtUtc: string | null
  reviewedByUserId: string | null
}

function parseGuidString(x: unknown): string {
  if (typeof x === 'string' && x.trim()) return x.trim()
  throw new Error('Invalid PTO payload')
}

function parsePtoRequestRow(r: Record<string, unknown>): PtoRequestRow {
  const secId = r.secondaryApproverUserId
  const secEmail = r.secondaryApproverEmail
  return {
    id: parseGuidString(r.id),
    userId: parseGuidString(r.userId),
    userEmail: typeof r.userEmail === 'string' ? r.userEmail : '',
    startDate: typeof r.startDate === 'string' ? r.startDate : '',
    endDate: typeof r.endDate === 'string' ? r.endDate : '',
    reason: typeof r.reason === 'string' ? r.reason : '',
    status: typeof r.status === 'string' ? r.status : '',
    approverUserId: parseGuidString(r.approverUserId),
    approverEmail: typeof r.approverEmail === 'string' ? r.approverEmail : '',
    secondaryApproverUserId:
      typeof secId === 'string' && secId.trim() ? secId.trim() : secId === null ? null : undefined,
    secondaryApproverEmail:
      typeof secEmail === 'string' && secEmail.trim()
        ? secEmail.trim()
        : secEmail === null
          ? null
          : undefined,
    createdAtUtc: typeof r.createdAtUtc === 'string' ? r.createdAtUtc : String(r.createdAtUtc ?? ''),
    reviewedAtUtc:
      r.reviewedAtUtc === null || typeof r.reviewedAtUtc === 'undefined'
        ? null
        : typeof r.reviewedAtUtc === 'string'
          ? r.reviewedAtUtc
          : null,
    reviewedByUserId:
      r.reviewedByUserId === null || typeof r.reviewedByUserId === 'undefined'
        ? null
        : typeof r.reviewedByUserId === 'string'
          ? r.reviewedByUserId
          : String(r.reviewedByUserId),
  }
}

export async function createPtoRequest(
  token: string,
  body: { startDate: string; endDate: string; reason?: string },
): Promise<PtoRequestRow> {
  const res = await fetch(`${base}/api/pto-requests`, {
    method: 'POST',
    headers: authHeaders(token),
    body: JSON.stringify(body),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not submit PTO request'))
  const r = (await res.json()) as Record<string, unknown>
  return parsePtoRequestRow(r)
}

export async function listMyPtoRequests(token: string): Promise<PtoRequestRow[]> {
  const res = await fetch(`${base}/api/pto-requests/mine`, { headers: { Authorization: `Bearer ${token}` } })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load PTO requests'))
  const data = (await res.json()) as unknown
  if (!Array.isArray(data)) throw new Error('Could not load PTO requests')
  return data.map((row) => parsePtoRequestRow(row as Record<string, unknown>))
}

export async function listPendingPtoRequests(token: string): Promise<PtoRequestRow[]> {
  const res = await fetch(`${base}/api/pto-requests/pending-approval`, {
    headers: { Authorization: `Bearer ${token}` },
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load pending PTO'))
  const data = (await res.json()) as unknown
  if (!Array.isArray(data)) throw new Error('Could not load pending PTO')
  return data.map((row) => parsePtoRequestRow(row as Record<string, unknown>))
}

export async function approvePtoRequest(token: string, id: string): Promise<void> {
  const res = await fetch(`${base}/api/pto-requests/${encodeURIComponent(id)}/approve`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
    body: '{}',
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not approve PTO'))
}

export async function rejectPtoRequest(token: string, id: string): Promise<void> {
  const res = await fetch(`${base}/api/pto-requests/${encodeURIComponent(id)}/reject`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
    body: '{}',
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not reject PTO'))
}

export type PersonalSummary = {
  from: string
  to: string
  totalHours: number
  billableHours: number
  nonBillableHours: number
  timesheetLineCount: number
  expensePendingTotal: number
  expenseApprovedTotal: number
  expenseRejectedTotal: number
  expenseCount: number
}

export async function getPersonalSummary(token: string, from: string, to: string): Promise<PersonalSummary> {
  const qs = new URLSearchParams({ from, to })
  const res = await fetch(`${base}/api/reports/personal-summary?${qs}`, {
    headers: { Authorization: `Bearer ${token}` },
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load report'))
  const r = (await res.json()) as Record<string, unknown>
  const nums = [
    'totalHours',
    'billableHours',
    'nonBillableHours',
    'expensePendingTotal',
    'expenseApprovedTotal',
    'expenseRejectedTotal',
  ] as const
  for (const k of nums) {
    if (typeof r[k] !== 'number') throw new Error('Could not load report')
  }
  if (
    typeof r.from !== 'string' ||
    typeof r.to !== 'string' ||
    typeof r.timesheetLineCount !== 'number' ||
    typeof r.expenseCount !== 'number'
  )
    throw new Error('Could not load report')
  return {
    from: r.from,
    to: r.to,
    totalHours: r.totalHours as number,
    billableHours: r.billableHours as number,
    nonBillableHours: r.nonBillableHours as number,
    timesheetLineCount: r.timesheetLineCount,
    expensePendingTotal: r.expensePendingTotal as number,
    expenseApprovedTotal: r.expenseApprovedTotal as number,
    expenseRejectedTotal: r.expenseRejectedTotal as number,
    expenseCount: r.expenseCount,
  }
}

export type ClientRow = {
  id: string
  name: string
  contactName: string | null
  contactEmail: string | null
  contactPhone: string | null
  defaultBillingRate: number | null
  notes: string | null
  isActive: boolean
  projects: { id: string; name: string }[]
  /** Present for Finance callers: roster or project-assigned finance for this client. */
  financePortfolioMember?: boolean | null
}

function parseFiniteNumber(x: unknown): number | null {
  if (typeof x === 'number' && Number.isFinite(x)) return x
  if (typeof x === 'string' && x.trim() !== '') {
    const n = Number(x)
    if (Number.isFinite(n)) return n
  }
  return null
}

function assertClient(x: unknown): ClientRow {
  const r = x as Record<string, unknown>
  if (typeof r.id !== 'string' || typeof r.name !== 'string' || typeof r.isActive !== 'boolean')
    throw new Error('Could not load client')
  const projectsRaw = r.projects
  const projects = Array.isArray(projectsRaw) ? projectsRaw : []
  const rate = parseFiniteNumber(r.defaultBillingRate)
  const fpm = r.financePortfolioMember
  return {
    id: r.id,
    name: r.name,
    contactName: typeof r.contactName === 'string' ? r.contactName : null,
    contactEmail: typeof r.contactEmail === 'string' ? r.contactEmail : null,
    contactPhone: typeof r.contactPhone === 'string' ? r.contactPhone : null,
    defaultBillingRate: rate,
    notes: typeof r.notes === 'string' ? r.notes : null,
    isActive: r.isActive,
    financePortfolioMember: typeof fpm === 'boolean' ? fpm : undefined,
    projects: projects.map((p) => {
      const o = p as Record<string, unknown>
      const id = typeof o.id === 'string' ? o.id : o.id != null ? String(o.id) : ''
      const name = typeof o.name === 'string' ? o.name : o.name != null ? String(o.name) : ''
      if (!id) throw new Error('Could not load client')
      return { id, name }
    }),
  }
}

export async function listClients(token: string, q?: string, includeInactive?: boolean): Promise<ClientRow[]> {
  const params = new URLSearchParams()
  if (q?.trim()) params.set('q', q.trim())
  if (includeInactive) params.set('includeInactive', 'true')
  const qs = params.toString()
  const url = qs ? `${base}/api/clients?${qs}` : `${base}/api/clients`
  const res = await fetch(url, { headers: { Authorization: `Bearer ${token}` } })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load clients'))
  const data = (await res.json()) as unknown
  if (!Array.isArray(data)) throw new Error('Could not load clients')
  return data.map(assertClient)
}

export async function createClient(
  token: string,
  body: {
    name: string
    contactName?: string
    contactEmail?: string
    contactPhone?: string
    defaultBillingRate?: number
    notes?: string
    financeLeadUserId?: string
  },
): Promise<ClientRow> {
  const res = await fetch(`${base}/api/clients`, {
    method: 'POST',
    headers: authHeaders(token),
    body: JSON.stringify(body),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not create client'))
  return assertClient(await res.json())
}

export async function patchClient(
  token: string,
  id: string,
  body: {
    name?: string
    contactName?: string | null
    contactEmail?: string | null
    contactPhone?: string | null
    defaultBillingRate?: number | null
    notes?: string | null
    isActive?: boolean
  },
): Promise<ClientRow> {
  const res = await fetch(`${base}/api/clients/${id}`, {
    method: 'PATCH',
    headers: authHeaders(token),
    body: JSON.stringify(body),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not update client'))
  return assertClient(await res.json())
}

export type ProjectRow = {
  id: string
  name: string
  clientId: string
  clientName: string
  budgetAmount: number
  isActive: boolean
  deliveryManagerUserId?: string | null
  engagementPartnerUserId?: string | null
  assignedFinanceUserId?: string | null
  teamMemberUserIds?: string[]
}

function parseTeamMemberIds(raw: unknown): string[] {
  if (!Array.isArray(raw)) return []
  const out: string[] = []
  for (const x of raw) {
    if (typeof x === 'string') out.push(x)
    else if (typeof x === 'number') out.push(String(x))
  }
  return out
}

export type AssignmentRow = {
  userId: string
  email: string
  displayName: string
  role: string
  /** Present from directory list; org manager (Manager role). */
  managerUserId?: string | null
}

export type StaffingRecommendationRow = {
  userId: string
  email: string
  displayName: string
  role: string
  totalScore: number
  skillScore: number
  availabilityScore: number
  utilizationScore: number
  rationale: string[]
  fallbackReason: string | null
}

export type StaffingRecommendationResponse = {
  fallbackMode: string
  warningMessage: string | null
  results: StaffingRecommendationRow[]
}

function assertAssignment(x: unknown): AssignmentRow {
  const r = x as Record<string, unknown>
  if (
    typeof r.userId !== 'string' ||
    typeof r.email !== 'string' ||
    typeof r.displayName !== 'string' ||
    typeof r.role !== 'string'
  )
    throw new Error('Could not load assignments')
  let managerUserId: string | null | undefined
  if ('managerUserId' in r) {
    const mid = r.managerUserId
    managerUserId = mid === null ? null : typeof mid === 'string' ? mid : undefined
  }
  return {
    userId: r.userId,
    email: r.email,
    displayName: r.displayName,
    role: r.role,
    managerUserId,
  }
}

async function readAssignmentRows(res: Response): Promise<AssignmentRow[]> {
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load assignments'))
  const data = (await res.json()) as unknown
  if (!Array.isArray(data)) throw new Error('Could not load assignments')
  return data.map(assertAssignment)
}

export async function listAssignableEmployees(token: string): Promise<AssignmentRow[]> {
  const res = await fetch(`${base}/api/assignments/employees`, {
    headers: { Authorization: `Bearer ${token}` },
  })
  return readAssignmentRows(res)
}

export async function listClientAssignments(token: string, clientId: string): Promise<AssignmentRow[]> {
  const res = await fetch(`${base}/api/assignments/clients/${encodeURIComponent(clientId)}`, {
    headers: { Authorization: `Bearer ${token}` },
  })
  return readAssignmentRows(res)
}

export async function assignEmployeeToClient(token: string, clientId: string, userId: string): Promise<void> {
  const res = await fetch(
    `${base}/api/assignments/clients/${encodeURIComponent(clientId)}/employees/${encodeURIComponent(userId)}`,
    { method: 'PUT', headers: { Authorization: `Bearer ${token}` } },
  )
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not assign employee to client'))
}

export async function unassignEmployeeFromClient(token: string, clientId: string, userId: string): Promise<void> {
  const res = await fetch(
    `${base}/api/assignments/clients/${encodeURIComponent(clientId)}/employees/${encodeURIComponent(userId)}`,
    { method: 'DELETE', headers: { Authorization: `Bearer ${token}` } },
  )
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not unassign employee from client'))
}

export async function listProjectAssignments(token: string, projectId: string): Promise<AssignmentRow[]> {
  const res = await fetch(`${base}/api/assignments/projects/${encodeURIComponent(projectId)}`, {
    headers: { Authorization: `Bearer ${token}` },
  })
  return readAssignmentRows(res)
}

export async function assignEmployeeToProject(token: string, projectId: string, userId: string): Promise<void> {
  const res = await fetch(
    `${base}/api/assignments/projects/${encodeURIComponent(projectId)}/employees/${encodeURIComponent(userId)}`,
    { method: 'PUT', headers: { Authorization: `Bearer ${token}` } },
  )
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not assign employee to project'))
}

export async function unassignEmployeeFromProject(token: string, projectId: string, userId: string): Promise<void> {
  const res = await fetch(
    `${base}/api/assignments/projects/${encodeURIComponent(projectId)}/employees/${encodeURIComponent(userId)}`,
    { method: 'DELETE', headers: { Authorization: `Bearer ${token}` } },
  )
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not unassign employee from project'))
}

export type ProjectTaskRow = {
  id: string
  projectId: string
  clientName: string
  projectName: string
  title: string
  description: string | null
  requiredSkills: string[]
  dueDate: string | null
  assignedUserId: string | null
  assignedEmail: string | null
  status: string
  createdByUserId: string
  createdAtUtc: string
  updatedAtUtc: string
}

function assertProjectTaskRow(x: unknown): ProjectTaskRow {
  const r = x as Record<string, unknown>
  const skills = r.requiredSkills
  if (
    typeof r.id !== 'string' ||
    typeof r.projectId !== 'string' ||
    typeof r.clientName !== 'string' ||
    typeof r.projectName !== 'string' ||
    typeof r.title !== 'string' ||
    typeof r.status !== 'string' ||
    typeof r.createdByUserId !== 'string' ||
    typeof r.createdAtUtc !== 'string' ||
    typeof r.updatedAtUtc !== 'string' ||
    !Array.isArray(skills)
  )
    throw new Error('Could not load project task')
  const parsedSkills = skills.filter((s): s is string => typeof s === 'string')
  return {
    id: r.id,
    projectId: r.projectId,
    clientName: r.clientName,
    projectName: r.projectName,
    title: r.title,
    description: typeof r.description === 'string' ? r.description : null,
    requiredSkills: parsedSkills,
    dueDate: typeof r.dueDate === 'string' ? r.dueDate : null,
    assignedUserId: typeof r.assignedUserId === 'string' ? r.assignedUserId : null,
    assignedEmail: typeof r.assignedEmail === 'string' ? r.assignedEmail : null,
    status: r.status,
    createdByUserId: r.createdByUserId,
    createdAtUtc: r.createdAtUtc,
    updatedAtUtc: r.updatedAtUtc,
  }
}

export async function listProjectTasks(token: string, projectId?: string): Promise<ProjectTaskRow[]> {
  const qs = projectId ? `?projectId=${encodeURIComponent(projectId)}` : ''
  const res = await fetch(`${base}/api/project-tasks${qs}`, { headers: authHeaders(token) })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load project tasks'))
  const data = (await res.json()) as unknown
  if (!Array.isArray(data)) throw new Error('Could not load project tasks')
  return data.map(assertProjectTaskRow)
}

export async function createProjectTask(
  token: string,
  body: {
    projectId: string
    title: string
    description?: string | null
    requiredSkills?: string[]
    dueDate?: string | null
    assignedUserId?: string | null
  },
): Promise<ProjectTaskRow> {
  const res = await fetch(`${base}/api/project-tasks`, {
    method: 'POST',
    headers: authHeaders(token),
    body: JSON.stringify(body),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not create project task'))
  return assertProjectTaskRow(await res.json())
}

export async function patchProjectTask(
  token: string,
  id: string,
  body: {
    title?: string
    description?: string | null
    requiredSkills?: string[]
    dueDate?: string | null
    assignedUserId?: string | null
    clearAssignedUser?: boolean
    status?: string
  },
): Promise<ProjectTaskRow> {
  const res = await fetch(`${base}/api/project-tasks/${encodeURIComponent(id)}`, {
    method: 'PATCH',
    headers: authHeaders(token),
    body: JSON.stringify(body),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not update project task'))
  return assertProjectTaskRow(await res.json())
}

export async function deleteProjectTask(token: string, id: string): Promise<void> {
  const res = await fetch(`${base}/api/project-tasks/${encodeURIComponent(id)}`, {
    method: 'DELETE',
    headers: authHeaders(token),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not delete project task'))
}

function assertRecommendationRow(x: unknown): StaffingRecommendationRow {
  const r = x as Record<string, unknown>
  if (
    typeof r.userId !== 'string' ||
    typeof r.email !== 'string' ||
    typeof r.displayName !== 'string' ||
    typeof r.role !== 'string' ||
    typeof r.totalScore !== 'number' ||
    typeof r.skillScore !== 'number' ||
    typeof r.availabilityScore !== 'number' ||
    typeof r.utilizationScore !== 'number' ||
    !Array.isArray(r.rationale)
  )
    throw new Error('Could not load recommendations')
  const rationale = r.rationale.filter((v): v is string => typeof v === 'string')
  if (rationale.length !== r.rationale.length) throw new Error('Could not load recommendations')
  return {
    userId: r.userId,
    email: r.email,
    displayName: r.displayName,
    role: r.role,
    totalScore: r.totalScore,
    skillScore: r.skillScore,
    availabilityScore: r.availabilityScore,
    utilizationScore: r.utilizationScore,
    rationale,
    fallbackReason: typeof r.fallbackReason === 'string' ? r.fallbackReason : null,
  }
}

function parseStaffingRecommendationPayload(raw: unknown): StaffingRecommendationResponse {
  const data = raw as Record<string, unknown>
  if (typeof data.fallbackMode !== 'string' || !Array.isArray(data.results))
    throw new Error('Could not load recommendations')
  return {
    fallbackMode: data.fallbackMode,
    warningMessage: typeof data.warningMessage === 'string' ? data.warningMessage : null,
    results: data.results.map(assertRecommendationRow),
  }
}

export async function getProjectStaffingRecommendations(
  token: string,
  projectId: string,
  requiredSkills: string[],
): Promise<StaffingRecommendationResponse> {
  const res = await fetch(`${base}/api/assignments/projects/${encodeURIComponent(projectId)}/recommendations`, {
    method: 'POST',
    headers: authHeaders(token),
    body: JSON.stringify({ requiredSkills }),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load recommendations'))
  return parseStaffingRecommendationPayload(await res.json())
}

/** Uses the task’s project + required skills (same engine as project staffing recommendations). */
export async function recommendProjectTask(token: string, taskId: string): Promise<StaffingRecommendationResponse> {
  const res = await fetch(`${base}/api/project-tasks/${encodeURIComponent(taskId)}/recommendations`, {
    method: 'POST',
    headers: authHeaders(token),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load recommendations'))
  return parseStaffingRecommendationPayload(await res.json())
}

function assertProject(x: unknown): ProjectRow {
  const r = x as Record<string, unknown>
  const id = typeof r.id === 'string' ? r.id : r.id != null ? String(r.id) : ''
  const clientId = typeof r.clientId === 'string' ? r.clientId : r.clientId != null ? String(r.clientId) : ''
  const budget = parseFiniteNumber(r.budgetAmount)
  if (
    !id ||
    typeof r.name !== 'string' ||
    !clientId ||
    typeof r.clientName !== 'string' ||
    budget === null ||
    typeof r.isActive !== 'boolean'
  )
    throw new Error('Could not load project')
  return {
    id,
    name: r.name,
    clientId,
    clientName: r.clientName,
    budgetAmount: budget,
    isActive: r.isActive,
    deliveryManagerUserId: typeof r.deliveryManagerUserId === 'string' ? r.deliveryManagerUserId : null,
    engagementPartnerUserId: typeof r.engagementPartnerUserId === 'string' ? r.engagementPartnerUserId : null,
    assignedFinanceUserId: typeof r.assignedFinanceUserId === 'string' ? r.assignedFinanceUserId : null,
    teamMemberUserIds: parseTeamMemberIds(r.teamMemberUserIds),
  }
}

export type ProjectStaffingUserRow = {
  id: string
  email: string
  displayName: string
  role: string
}

function appRoleFromApi(raw: unknown): string {
  if (typeof raw === 'string') return raw
  if (typeof raw === 'number' && Number.isInteger(raw) && raw >= 0 && raw <= 4) {
    const names = ['IC', 'Admin', 'Manager', 'Finance', 'Partner'] as const
    return names[raw] ?? String(raw)
  }
  throw new Error('Could not load staffing user')
}

function assertProjectStaffingUser(x: unknown): ProjectStaffingUserRow {
  const r = x as Record<string, unknown>
  if (typeof r.id !== 'string' || typeof r.email !== 'string' || typeof r.displayName !== 'string')
    throw new Error('Could not load staffing user')
  return { id: r.id, email: r.email, displayName: r.displayName, role: appRoleFromApi(r.role) }
}

export async function listProjectStaffingUsers(token: string): Promise<ProjectStaffingUserRow[]> {
  const res = await fetch(`${base}/api/projects/staffing-users`, { headers: authHeaders(token) })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load staffing users'))
  const data = (await res.json()) as unknown
  if (!Array.isArray(data)) throw new Error('Could not load staffing users')
  return data.map(assertProjectStaffingUser)
}

export async function getProject(token: string, id: string): Promise<ProjectRow> {
  const res = await fetch(`${base}/api/projects/${encodeURIComponent(id)}`, { headers: authHeaders(token) })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load project'))
  return assertProject(await res.json())
}

export type ProjectExpenseRow = {
  id: string
  userId: string
  submitterEmail: string
  expenseDate: string
  status: string
  amount: number
  category: string
  description: string
}

export type ProjectExpenseInsights = {
  clientName: string
  projectName: string
  budgetAmount: number
  pendingCount: number
  approvedCount: number
  rejectedCount: number
  pendingAmount: number
  approvedAmount: number
  rejectedAmount: number
  expenses: ProjectExpenseRow[]
}

function assertProjectExpenseRow(x: unknown): ProjectExpenseRow {
  const r = x as Record<string, unknown>
  if (
    typeof r.id !== 'string' ||
    typeof r.userId !== 'string' ||
    typeof r.submitterEmail !== 'string' ||
    typeof r.expenseDate !== 'string' ||
    typeof r.status !== 'string' ||
    typeof r.amount !== 'number' ||
    typeof r.category !== 'string' ||
    typeof r.description !== 'string'
  )
    throw new Error('Could not load expense row')
  return {
    id: r.id,
    userId: r.userId,
    submitterEmail: r.submitterEmail,
    expenseDate: r.expenseDate,
    status: r.status,
    amount: r.amount,
    category: r.category,
    description: r.description,
  }
}

function assertProjectExpenseInsights(x: unknown): ProjectExpenseInsights {
  const r = x as Record<string, unknown>
  const ex = r.expenses
  if (
    typeof r.clientName !== 'string' ||
    typeof r.projectName !== 'string' ||
    typeof r.budgetAmount !== 'number' ||
    typeof r.pendingCount !== 'number' ||
    typeof r.approvedCount !== 'number' ||
    typeof r.rejectedCount !== 'number' ||
    typeof r.pendingAmount !== 'number' ||
    typeof r.approvedAmount !== 'number' ||
    typeof r.rejectedAmount !== 'number' ||
    !Array.isArray(ex)
  )
    throw new Error('Could not load project expense insights')
  return {
    clientName: r.clientName,
    projectName: r.projectName,
    budgetAmount: r.budgetAmount,
    pendingCount: r.pendingCount,
    approvedCount: r.approvedCount,
    rejectedCount: r.rejectedCount,
    pendingAmount: r.pendingAmount,
    approvedAmount: r.approvedAmount,
    rejectedAmount: r.rejectedAmount,
    expenses: ex.map(assertProjectExpenseRow),
  }
}

export async function getProjectExpenseInsights(token: string, projectId: string): Promise<ProjectExpenseInsights> {
  const res = await fetch(`${base}/api/projects/${encodeURIComponent(projectId)}/expense-insights`, {
    headers: authHeaders(token),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load project expenses'))
  return assertProjectExpenseInsights(await res.json())
}

export async function listProjects(
  token: string,
  args?: { q?: string; clientId?: string; includeInactive?: boolean },
): Promise<ProjectRow[]> {
  const params = new URLSearchParams()
  if (args?.q?.trim()) params.set('q', args.q.trim())
  if (args?.clientId) params.set('clientId', args.clientId)
  if (args?.includeInactive) params.set('includeInactive', 'true')
  const qs = params.toString()
  const url = qs ? `${base}/api/projects?${qs}` : `${base}/api/projects`
  const res = await fetch(url, { headers: { Authorization: `Bearer ${token}` } })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load projects'))
  const data = (await res.json()) as unknown
  if (!Array.isArray(data)) throw new Error('Could not load projects')
  return data.map(assertProject)
}

export async function createProject(
  token: string,
  body: {
    name: string
    clientId: string
    budgetAmount: number
    deliveryManagerUserId?: string | null
    engagementPartnerUserId?: string | null
    assignedFinanceUserId?: string | null
    teamMemberUserIds?: string[]
  },
): Promise<ProjectRow> {
  const res = await fetch(`${base}/api/projects`, {
    method: 'POST',
    headers: authHeaders(token),
    body: JSON.stringify(body),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not create project'))
  return assertProject(await res.json())
}

export async function patchProject(
  token: string,
  id: string,
  body: {
    name?: string
    clientId?: string
    budgetAmount?: number
    isActive?: boolean
    deliveryManagerUserId?: string | null
    engagementPartnerUserId?: string | null
    assignedFinanceUserId?: string | null
    clearDeliveryManager?: boolean
    clearEngagementPartner?: boolean
    clearAssignedFinance?: boolean
    teamMemberUserIds?: string[] | null
  },
): Promise<ProjectRow> {
  const res = await fetch(`${base}/api/projects/${id}`, {
    method: 'PATCH',
    headers: authHeaders(token),
    body: JSON.stringify(body),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not update project'))
  return assertProject(await res.json())
}

export type ExpenseRow = {
  id: string
  userId: string
  userEmail: string
  expenseDate: string
  client: string
  project: string
  category: string
  description: string
  amount: number
  status: 'Pending' | 'Approved' | 'Rejected'
  reviewedByEmail: string | null
  reviewedAtUtc: string | null
  hasInvoice: boolean
}

function assertExpense(x: unknown): ExpenseRow {
  const r = x as Record<string, unknown>
  if (
    typeof r.id !== 'string' ||
    typeof r.userId !== 'string' ||
    typeof r.userEmail !== 'string' ||
    typeof r.expenseDate !== 'string' ||
    typeof r.client !== 'string' ||
    typeof r.project !== 'string' ||
    typeof r.category !== 'string' ||
    typeof r.description !== 'string' ||
    typeof r.amount !== 'number' ||
    (r.status !== 'Pending' && r.status !== 'Approved' && r.status !== 'Rejected')
  ) {
    throw new Error('Could not load expenses')
  }
  return {
    id: r.id,
    userId: r.userId,
    userEmail: r.userEmail,
    expenseDate: r.expenseDate,
    client: r.client,
    project: r.project,
    category: r.category,
    description: r.description,
    amount: r.amount,
    status: r.status,
    reviewedByEmail: typeof r.reviewedByEmail === 'string' ? r.reviewedByEmail : null,
    reviewedAtUtc: typeof r.reviewedAtUtc === 'string' ? r.reviewedAtUtc : null,
    hasInvoice: typeof r.hasInvoice === 'boolean' ? r.hasInvoice : false,
  }
}

export async function listMyExpenses(token: string): Promise<ExpenseRow[]> {
  const res = await fetch(`${base}/api/expenses/mine`, { headers: { Authorization: `Bearer ${token}` } })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load expenses'))
  const data = (await res.json()) as unknown
  if (!Array.isArray(data)) throw new Error('Could not load expenses')
  return data.map(assertExpense)
}

/** Admin: all org expenses. Manager: direct reports only (all statuses, full line detail). */
export async function listTeamExpenses(token: string): Promise<ExpenseRow[]> {
  const res = await fetch(`${base}/api/expenses/team`, { headers: { Authorization: `Bearer ${token}` } })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load team expenses'))
  const data = (await res.json()) as unknown
  if (!Array.isArray(data)) throw new Error('Could not load team expenses')
  return data.map(assertExpense)
}

export async function createExpense(
  token: string,
  body: { expenseDate: string; client: string; project: string; category: string; description: string; amount: number },
  invoiceFile?: File | null,
): Promise<ExpenseRow> {
  const hasFile = invoiceFile && invoiceFile.size > 0
  const res = hasFile
    ? await fetch(`${base}/api/expenses`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}` },
        body: (() => {
          const fd = new FormData()
          fd.append('expenseDate', body.expenseDate)
          fd.append('client', body.client)
          fd.append('project', body.project)
          fd.append('category', body.category)
          fd.append('description', body.description)
          fd.append('amount', String(body.amount))
          fd.append('invoice', invoiceFile)
          return fd
        })(),
      })
    : await fetch(`${base}/api/expenses`, {
        method: 'POST',
        headers: authHeaders(token),
        body: JSON.stringify(body),
      })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not create expense'))
  return assertExpense(await res.json())
}

/** Opens a browser download of the attached invoice (PDF or image). */
export async function downloadExpenseInvoice(token: string, expenseId: string): Promise<void> {
  const res = await fetch(`${base}/api/expenses/${encodeURIComponent(expenseId)}/invoice`, {
    headers: { Authorization: `Bearer ${token}` },
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not download invoice'))
  const blob = await res.blob()
  const dispo = res.headers.get('Content-Disposition')
  let filename = 'invoice'
  const m = dispo?.match(/filename\*?=(?:UTF-8'')?("?)([^";\n]+)\1/i)
  if (m?.[2]) {
    try {
      filename = decodeURIComponent(m[2].trim())
    } catch {
      filename = m[2].trim()
    }
  }
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  a.rel = 'noopener'
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

export async function listPendingExpenseApprovals(token: string): Promise<ExpenseRow[]> {
  const res = await fetch(`${base}/api/expenses/approvals/pending`, { headers: { Authorization: `Bearer ${token}` } })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load approvals'))
  const data = (await res.json()) as unknown
  if (!Array.isArray(data)) throw new Error('Could not load approvals')
  return data.map(assertExpense)
}

export async function approveExpense(token: string, id: string): Promise<void> {
  const res = await fetch(`${base}/api/expenses/${id}/approve`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${token}` },
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not approve expense'))
}

export async function rejectExpense(token: string, id: string): Promise<void> {
  const res = await fetch(`${base}/api/expenses/${id}/reject`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${token}` },
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not reject expense'))
}

export async function listFinanceExpenseLedger(token: string): Promise<ExpenseRow[]> {
  const res = await fetch(`${base}/api/expenses/ledger`, { headers: { Authorization: `Bearer ${token}` } })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load expense register'))
  const data = (await res.json()) as unknown
  if (!Array.isArray(data)) throw new Error('Could not load expense register')
  return data.map(assertExpense)
}

export type IssuedInvoiceListItem = {
  id: string
  kind: string
  projectId: string
  projectName: string
  clientName: string
  payeeEmail: string | null
  periodStart: string
  periodEnd: string
  issueNumber: string
  issuedAtUtc: string
  totalAmount: number
}

export type IssueInvoiceResult = {
  invoiceId: string
  issueNumber: string
  totalAmount: number
  lineCount: number
}

function assertIssuedInvoiceListItem(x: unknown): IssuedInvoiceListItem {
  const r = x as Record<string, unknown>
  if (
    typeof r.id !== 'string' ||
    typeof r.kind !== 'string' ||
    typeof r.projectId !== 'string' ||
    typeof r.projectName !== 'string' ||
    typeof r.clientName !== 'string' ||
    typeof r.periodStart !== 'string' ||
    typeof r.periodEnd !== 'string' ||
    typeof r.issueNumber !== 'string' ||
    typeof r.issuedAtUtc !== 'string' ||
    typeof r.totalAmount !== 'number'
  )
    throw new Error('Could not load issued invoices')
  return {
    id: r.id,
    kind: r.kind,
    projectId: r.projectId,
    projectName: r.projectName,
    clientName: r.clientName,
    payeeEmail: typeof r.payeeEmail === 'string' ? r.payeeEmail : null,
    periodStart: r.periodStart,
    periodEnd: r.periodEnd,
    issueNumber: r.issueNumber,
    issuedAtUtc: r.issuedAtUtc,
    totalAmount: r.totalAmount,
  }
}

function assertIssueInvoiceResult(x: unknown): IssueInvoiceResult {
  const r = x as Record<string, unknown>
  if (
    typeof r.invoiceId !== 'string' ||
    typeof r.issueNumber !== 'string' ||
    typeof r.totalAmount !== 'number' ||
    typeof r.lineCount !== 'number'
  )
    throw new Error('Could not issue invoice')
  return {
    invoiceId: r.invoiceId,
    issueNumber: r.issueNumber,
    totalAmount: r.totalAmount,
    lineCount: r.lineCount,
  }
}

export async function listIssuedInvoices(token: string, projectId?: string): Promise<IssuedInvoiceListItem[]> {
  const qs = projectId ? `?projectId=${encodeURIComponent(projectId)}` : ''
  const res = await fetch(`${base}/api/invoices/issued${qs}`, { headers: authHeaders(token) })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load issued invoices'))
  const data = (await res.json()) as unknown
  if (!Array.isArray(data)) throw new Error('Could not load issued invoices')
  return data.map(assertIssuedInvoiceListItem)
}

export async function issueProjectApprovedExpensesInvoice(
  token: string,
  body: { projectId: string; periodStart: string; periodEnd: string },
): Promise<IssueInvoiceResult> {
  const res = await fetch(`${base}/api/invoices/issue-project-approved-expenses`, {
    method: 'POST',
    headers: authHeaders(token),
    body: JSON.stringify({
      projectId: body.projectId,
      periodStart: body.periodStart,
      periodEnd: body.periodEnd,
    }),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not issue invoice'))
  return assertIssueInvoiceResult(await res.json())
}

export async function issueProjectPayoutInvoicesByUser(
  token: string,
  body: { projectId: string; periodStart: string; periodEnd: string },
): Promise<IssueInvoiceResult[]> {
  const res = await fetch(`${base}/api/invoices/issue-project-payouts-by-user`, {
    method: 'POST',
    headers: authHeaders(token),
    body: JSON.stringify({
      projectId: body.projectId,
      periodStart: body.periodStart,
      periodEnd: body.periodEnd,
    }),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not issue payout invoices'))
  const data = (await res.json()) as Record<string, unknown>
  const inv = data.invoices
  if (!Array.isArray(inv)) throw new Error('Could not issue payout invoices')
  return inv.map(assertIssueInvoiceResult)
}

/** Opens printable HTML (use browser Print / Save as PDF). */
export async function openIssuedInvoicePrint(token: string, invoiceId: string): Promise<void> {
  const res = await fetch(`${base}/api/invoices/${encodeURIComponent(invoiceId)}/print`, {
    headers: authHeaders(token),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load invoice for print'))
  const html = await res.text()
  const w = window.open('', '_blank')
  if (!w) throw new Error('Popup blocked — allow popups for print view')
  w.document.open()
  w.document.write(html)
  w.document.close()
}

export type FinanceExpenseAiResponse = { narrative: string; source: string }

export async function fetchFinanceExpenseNarrative(
  token: string,
  body: { projectId: string; periodStart: string; periodEnd: string },
): Promise<FinanceExpenseAiResponse> {
  const res = await fetch(`${base}/api/finance/insights/expense-narrative`, {
    method: 'POST',
    headers: authHeaders(token),
    body: JSON.stringify(body),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not build AI narrative'))
  const r = (await res.json()) as Record<string, unknown>
  if (typeof r.narrative !== 'string' || typeof r.source !== 'string') throw new Error('Could not build AI narrative')
  return { narrative: r.narrative, source: r.source }
}

export type QuoteRow = {
  id: string
  clientId: string
  clientName: string
  referenceNumber: string
  title: string
  scopeSummary: string | null
  estimatedHours: number
  hourlyRate: number
  totalAmount: number
  status: string
  validThrough: string | null
  createdAtUtc: string
}

function assertQuote(x: unknown): QuoteRow {
  const r = x as Record<string, unknown>
  if (
    typeof r.id !== 'string' ||
    typeof r.clientId !== 'string' ||
    typeof r.clientName !== 'string' ||
    typeof r.referenceNumber !== 'string' ||
    typeof r.title !== 'string' ||
    typeof r.estimatedHours !== 'number' ||
    typeof r.hourlyRate !== 'number' ||
    typeof r.totalAmount !== 'number' ||
    typeof r.status !== 'string' ||
    typeof r.createdAtUtc !== 'string'
  )
    throw new Error('Could not load quotes')
  return {
    id: r.id,
    clientId: r.clientId,
    clientName: r.clientName,
    referenceNumber: r.referenceNumber,
    title: r.title,
    scopeSummary: typeof r.scopeSummary === 'string' ? r.scopeSummary : null,
    estimatedHours: r.estimatedHours,
    hourlyRate: r.hourlyRate,
    totalAmount: r.totalAmount,
    status: r.status,
    validThrough: typeof r.validThrough === 'string' ? r.validThrough : null,
    createdAtUtc: r.createdAtUtc,
  }
}

export async function listQuotes(token: string): Promise<QuoteRow[]> {
  const res = await fetch(`${base}/api/quotes`, { headers: { Authorization: `Bearer ${token}` } })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load quotes'))
  const data = (await res.json()) as unknown
  if (!Array.isArray(data)) throw new Error('Could not load quotes')
  return data.map(assertQuote)
}

export async function createQuote(
  token: string,
  body: {
    clientId: string
    title: string
    scopeSummary?: string
    estimatedHours: number
    hourlyRate: number
    validThrough?: string
    status?: 'Draft' | 'Sent'
  },
): Promise<QuoteRow> {
  const res = await fetch(`${base}/api/quotes`, {
    method: 'POST',
    headers: authHeaders(token),
    body: JSON.stringify(body),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not create quote'))
  return assertQuote(await res.json())
}

export type TimesheetLine = {
  workDate: string // YYYY-MM-DD
  client: string
  project: string
  task: string
  hours: number
  isBillable: boolean
  notes: string | null
}

function assertTimesheetLine(x: unknown): TimesheetLine {
  const r = x as Record<string, unknown>
  if (
    typeof r.workDate !== 'string' ||
    typeof r.client !== 'string' ||
    typeof r.project !== 'string' ||
    typeof r.task !== 'string' ||
    typeof r.hours !== 'number' ||
    typeof r.isBillable !== 'boolean' ||
    !(typeof r.notes === 'string' || r.notes === null || r.notes === undefined)
  )
    throw new Error('Could not load timesheet')
  return {
    workDate: r.workDate,
    client: r.client,
    project: r.project,
    task: r.task,
    hours: r.hours,
    isBillable: r.isBillable,
    notes: typeof r.notes === 'string' ? r.notes : null,
  }
}

export async function getTimesheetWeek(token: string, weekStart: string): Promise<TimesheetLine[]> {
  const res = await fetch(`${base}/api/timesheets/week?weekStart=${encodeURIComponent(weekStart)}`, {
    headers: { Authorization: `Bearer ${token}` },
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load timesheet'))
  const data = (await res.json()) as unknown
  if (!Array.isArray(data)) throw new Error('Could not load timesheet')
  return data.map(assertTimesheetLine)
}

export async function putTimesheetWeek(token: string, weekStart: string, lines: TimesheetLine[]): Promise<void> {
  const res = await fetch(`${base}/api/timesheets/week?weekStart=${encodeURIComponent(weekStart)}`, {
    method: 'PUT',
    headers: authHeaders(token),
    body: JSON.stringify(lines),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not save timesheet'))
}

export type TimesheetWeekApprovalStatus = 'None' | 'Pending' | 'Approved' | 'Rejected'

export type TimesheetWeekStatusPayload = {
  weekStart: string
  status: TimesheetWeekApprovalStatus
  /** Current sum of hours on the timesheet grid for the week. */
  totalHours: number
  billableHours: number
  /** When status is Pending: hours frozen at submit (same as total unless legacy data). */
  pendingSubmissionTotalHours: number | null
  pendingSubmissionBillableHours: number | null
  submittedAtUtc: string | null
  reviewedAtUtc: string | null
}

function assertTimesheetWeekStatus(x: unknown): TimesheetWeekStatusPayload {
  const r = x as Record<string, unknown>
  if (
    typeof r.weekStart !== 'string' ||
    typeof r.status !== 'string' ||
    typeof r.totalHours !== 'number' ||
    typeof r.billableHours !== 'number'
  )
    throw new Error('Could not load timesheet week status')
  const st = r.status
  if (st !== 'None' && st !== 'Pending' && st !== 'Approved' && st !== 'Rejected')
    throw new Error('Could not load timesheet week status')
  const pst = r.pendingSubmissionTotalHours
  const psb = r.pendingSubmissionBillableHours
  return {
    weekStart: r.weekStart,
    status: st,
    totalHours: r.totalHours,
    billableHours: r.billableHours,
    pendingSubmissionTotalHours: typeof pst === 'number' && Number.isFinite(pst) ? pst : null,
    pendingSubmissionBillableHours: typeof psb === 'number' && Number.isFinite(psb) ? psb : null,
    submittedAtUtc: typeof r.submittedAtUtc === 'string' ? r.submittedAtUtc : null,
    reviewedAtUtc: typeof r.reviewedAtUtc === 'string' ? r.reviewedAtUtc : null,
  }
}

export async function getTimesheetWeekStatus(token: string, weekStart: string): Promise<TimesheetWeekStatusPayload> {
  const res = await fetch(`${base}/api/timesheets/week/status?weekStart=${encodeURIComponent(weekStart)}`, {
    headers: authHeaders(token),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load timesheet week status'))
  return assertTimesheetWeekStatus(await res.json())
}

export async function submitTimesheetWeekForApproval(token: string, weekStart: string): Promise<void> {
  const res = await fetch(`${base}/api/timesheets/week/submit?weekStart=${encodeURIComponent(weekStart)}`, {
    method: 'POST',
    headers: authHeaders(token),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not submit timesheet for approval'))
}

export type PendingTimesheetWeek = {
  userId: string
  userEmail: string
  weekStart: string
  totalHours: number
  billableHours: number
  submittedAtUtc: string
}

function assertPendingTimesheetWeek(x: unknown): PendingTimesheetWeek {
  const r = x as Record<string, unknown>
  if (
    typeof r.userId !== 'string' ||
    typeof r.userEmail !== 'string' ||
    typeof r.weekStart !== 'string' ||
    typeof r.totalHours !== 'number' ||
    typeof r.billableHours !== 'number' ||
    typeof r.submittedAtUtc !== 'string'
  )
    throw new Error('Could not load pending timesheet approvals')
  return {
    userId: r.userId,
    userEmail: r.userEmail,
    weekStart: r.weekStart,
    totalHours: r.totalHours,
    billableHours: r.billableHours,
    submittedAtUtc: r.submittedAtUtc,
  }
}

export async function listPendingTimesheetWeekApprovals(token: string): Promise<PendingTimesheetWeek[]> {
  const res = await fetch(`${base}/api/timesheets/approvals/pending-weeks`, {
    headers: authHeaders(token),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load pending timesheet approvals'))
  const data = (await res.json()) as unknown
  if (!Array.isArray(data)) throw new Error('Could not load pending timesheet approvals')
  return data.map(assertPendingTimesheetWeek)
}

export async function approveTimesheetWeek(token: string, userId: string, weekStart: string): Promise<void> {
  const res = await fetch(
    `${base}/api/timesheets/approvals/week/${encodeURIComponent(userId)}/approve?weekStart=${encodeURIComponent(weekStart)}`,
    { method: 'POST', headers: authHeaders(token) },
  )
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not approve timesheet week'))
}

export async function rejectTimesheetWeek(token: string, userId: string, weekStart: string): Promise<void> {
  const res = await fetch(
    `${base}/api/timesheets/approvals/week/${encodeURIComponent(userId)}/reject?weekStart=${encodeURIComponent(weekStart)}`,
    { method: 'POST', headers: authHeaders(token) },
  )
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not reject timesheet week'))
}

export type ProjectBudgetBar = {
  clientName: string
  projectName: string
  budgetAmount: number
  defaultHourlyRate: number | null
  consumedBillableAmount: number
  pendingSubmissionBillableAmount: number
  pendingBillableHours: number
  catalogMatched: boolean
}

function assertProjectBudgetBar(x: unknown): ProjectBudgetBar {
  const r = x as Record<string, unknown>
  if (
    typeof r.clientName !== 'string' ||
    typeof r.projectName !== 'string' ||
    typeof r.budgetAmount !== 'number' ||
    typeof r.consumedBillableAmount !== 'number' ||
    typeof r.pendingSubmissionBillableAmount !== 'number' ||
    typeof r.pendingBillableHours !== 'number' ||
    typeof r.catalogMatched !== 'boolean'
  )
    throw new Error('Could not load project budget bar')
  const rate = r.defaultHourlyRate
  return {
    clientName: r.clientName,
    projectName: r.projectName,
    budgetAmount: r.budgetAmount,
    defaultHourlyRate: typeof rate === 'number' ? rate : null,
    consumedBillableAmount: r.consumedBillableAmount,
    pendingSubmissionBillableAmount: r.pendingSubmissionBillableAmount,
    pendingBillableHours: r.pendingBillableHours,
    catalogMatched: r.catalogMatched,
  }
}

export type TimesheetPendingWeekReview = {
  userId: string
  userEmail: string
  weekStart: string
  submittedAtUtc: string
  lines: TimesheetLine[]
  projectBudgetBars: ProjectBudgetBar[]
}

function assertTimesheetPendingWeekReview(x: unknown): TimesheetPendingWeekReview {
  const r = x as Record<string, unknown>
  const userId = typeof r.userId === 'string' ? r.userId : ''
  if (
    !userId ||
    typeof r.userEmail !== 'string' ||
    typeof r.weekStart !== 'string' ||
    typeof r.submittedAtUtc !== 'string' ||
    !Array.isArray(r.lines)
  )
    throw new Error('Could not load timesheet for review')
  const barsRaw = r.projectBudgetBars
  const projectBudgetBars = Array.isArray(barsRaw) ? barsRaw.map(assertProjectBudgetBar) : []
  return {
    userId,
    userEmail: r.userEmail,
    weekStart: r.weekStart,
    submittedAtUtc: r.submittedAtUtc,
    lines: r.lines.map(assertTimesheetLine),
    projectBudgetBars,
  }
}

export async function getPendingTimesheetWeekForReview(
  token: string,
  userId: string,
  weekStart: string,
): Promise<TimesheetPendingWeekReview> {
  const res = await fetch(
    `${base}/api/timesheets/approvals/week/${encodeURIComponent(userId)}/pending-review?weekStart=${encodeURIComponent(weekStart)}`,
    { headers: authHeaders(token) },
  )
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load timesheet for review'))
  return assertTimesheetPendingWeekReview(await res.json())
}

export type ResourceTrackerDay = {
  date: string
  status: 'Available' | 'SoftBooked' | 'FullyBooked' | 'PTO'
  hours: number
}

export type ResourceTrackerEmployeeRow = {
  userId: string
  email: string
  role: string
  days: ResourceTrackerDay[]
}

export async function getResourceTrackerMonth(token: string, monthStart: string): Promise<ResourceTrackerEmployeeRow[]> {
  const res = await fetch(`${base}/api/timesheets/organization?monthStart=${encodeURIComponent(monthStart)}`, {
    headers: { Authorization: `Bearer ${token}` },
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load Resource Tracker'))
  const data = (await res.json()) as unknown
  if (!Array.isArray(data)) throw new Error('Could not load Resource Tracker')
  return data.map((row) => {
    const r = row as Record<string, unknown>
    if (
      typeof r.userId !== 'string' ||
      typeof r.email !== 'string' ||
      typeof r.role !== 'string' ||
      !Array.isArray(r.days)
    )
      throw new Error('Could not load Resource Tracker')
    const days = r.days.map((d) => {
      const x = d as Record<string, unknown>
      if (
        typeof x.date !== 'string' ||
        typeof x.status !== 'string' ||
        typeof x.hours !== 'number' ||
        (x.status !== 'Available' && x.status !== 'SoftBooked' && x.status !== 'FullyBooked' && x.status !== 'PTO')
      )
        throw new Error('Could not load Resource Tracker')
      return { date: x.date, status: x.status, hours: x.hours } as ResourceTrackerDay
    })
    return { userId: r.userId, email: r.email, role: r.role, days }
  })
}

export type OperationsAiInsight = {
  severity: string
  code: string
  message: string
  source: string
}

export type OperationsExpenseAiReviewResult = {
  reviewKind: string
  submitterEmail: string | null
  usedLlm: boolean
  llmNote: string | null
  insights: OperationsAiInsight[]
  questionsForSubmitter: string[]
}

function assertOperationsAiInsight(x: unknown): OperationsAiInsight {
  const r = x as Record<string, unknown>
  if (
    typeof r.severity !== 'string' ||
    typeof r.code !== 'string' ||
    typeof r.message !== 'string' ||
    typeof r.source !== 'string'
  )
    throw new Error('Invalid AI review response')
  return {
    severity: r.severity,
    code: r.code,
    message: r.message,
    source: r.source,
  }
}

function assertExpenseAiReview(x: unknown): OperationsExpenseAiReviewResult {
  const r = x as Record<string, unknown>
  if (typeof r.usedLlm !== 'boolean' || !Array.isArray(r.insights) || !Array.isArray(r.questionsForSubmitter))
    throw new Error('Invalid expense AI review response')
  const llmNote = r.llmNote
  const rk = r.reviewKind
  const se = r.submitterEmail
  return {
    reviewKind: typeof rk === 'string' && rk.trim() ? rk : 'draft',
    submitterEmail: typeof se === 'string' ? se : null,
    usedLlm: r.usedLlm,
    llmNote: typeof llmNote === 'string' ? llmNote : null,
    insights: r.insights.map(assertOperationsAiInsight),
    questionsForSubmitter: r.questionsForSubmitter.filter((q): q is string => typeof q === 'string'),
  }
}

export async function reviewExpenseDraftAi(
  token: string,
  body: {
    expenseDate: string
    client: string
    project: string
    category: string
    description: string
    amount: number
    hasInvoiceAttachment: boolean
  },
): Promise<OperationsExpenseAiReviewResult> {
  const res = await fetch(`${base}/api/ai/operations/expense-review`, {
    method: 'POST',
    headers: authHeaders(token),
    body: JSON.stringify(body),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Expense AI review failed'))
  return assertExpenseAiReview(await res.json())
}

export async function reviewExpenseApproverAi(token: string, expenseId: string): Promise<OperationsExpenseAiReviewResult> {
  const res = await fetch(`${base}/api/ai/operations/expense-approver-review`, {
    method: 'POST',
    headers: authHeaders(token),
    body: JSON.stringify({ expenseId }),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Expense approver AI failed'))
  return assertExpenseAiReview(await res.json())
}

export type OperationsTimesheetWeekAiReviewResult = {
  reviewKind: string
  subjectEmail: string | null
  usedLlm: boolean
  llmNote: string | null
  weekTotalHours: number
  insights: OperationsAiInsight[]
  questionsForEmployee: string[]
  noteSuggestions: string[]
}

function assertTimesheetWeekAiReview(x: unknown): OperationsTimesheetWeekAiReviewResult {
  const r = x as Record<string, unknown>
  if (
    typeof r.usedLlm !== 'boolean' ||
    typeof r.weekTotalHours !== 'number' ||
    !Array.isArray(r.insights) ||
    !Array.isArray(r.questionsForEmployee) ||
    !Array.isArray(r.noteSuggestions)
  )
    throw new Error('Invalid timesheet AI review response')
  const llmNote = r.llmNote
  const rk = r.reviewKind
  const sub = r.subjectEmail
  return {
    reviewKind: typeof rk === 'string' && rk.trim() ? rk : 'draft',
    subjectEmail: typeof sub === 'string' ? sub : null,
    usedLlm: r.usedLlm,
    llmNote: typeof llmNote === 'string' ? llmNote : null,
    weekTotalHours: r.weekTotalHours,
    insights: r.insights.map(assertOperationsAiInsight),
    questionsForEmployee: r.questionsForEmployee.filter((q): q is string => typeof q === 'string'),
    noteSuggestions: r.noteSuggestions.filter((q): q is string => typeof q === 'string'),
  }
}

export async function reviewTimesheetWeekAi(
  token: string,
  weekStartMonday: string,
  lines: TimesheetLine[],
): Promise<OperationsTimesheetWeekAiReviewResult> {
  const res = await fetch(`${base}/api/ai/operations/timesheet-week-review`, {
    method: 'POST',
    headers: authHeaders(token),
    body: JSON.stringify({
      weekStartMonday,
      lines: lines.map((l) => ({
        workDate: l.workDate,
        client: l.client,
        project: l.project,
        task: l.task,
        hours: l.hours,
        isBillable: l.isBillable,
        notes: l.notes,
      })),
    }),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Timesheet AI review failed'))
  return assertTimesheetWeekAiReview(await res.json())
}

export async function reviewTimesheetApproverAi(
  token: string,
  userId: string,
  weekStartMonday: string,
): Promise<OperationsTimesheetWeekAiReviewResult> {
  const res = await fetch(`${base}/api/ai/operations/timesheet-approver-review`, {
    method: 'POST',
    headers: authHeaders(token),
    body: JSON.stringify({ userId, weekStartMonday }),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Timesheet approver AI failed'))
  return assertTimesheetWeekAiReview(await res.json())
}

export type FinanceLedgerAuditResult = {
  usedLlm: boolean
  llmNote: string | null
  rowCount: number
  totalPendingAmount: number
  totalApprovedAmount: number
  insights: OperationsAiInsight[]
  summaryPoints: string[]
}

function assertFinanceLedgerAudit(x: unknown): FinanceLedgerAuditResult {
  const r = x as Record<string, unknown>
  if (
    typeof r.usedLlm !== 'boolean' ||
    typeof r.rowCount !== 'number' ||
    typeof r.totalPendingAmount !== 'number' ||
    typeof r.totalApprovedAmount !== 'number' ||
    !Array.isArray(r.insights) ||
    !Array.isArray(r.summaryPoints)
  )
    throw new Error('Invalid ledger audit response')
  const ln = r.llmNote
  return {
    usedLlm: r.usedLlm,
    llmNote: typeof ln === 'string' ? ln : null,
    rowCount: r.rowCount,
    totalPendingAmount: r.totalPendingAmount,
    totalApprovedAmount: r.totalApprovedAmount,
    insights: r.insights.map(assertOperationsAiInsight),
    summaryPoints: r.summaryPoints.filter((q): q is string => typeof q === 'string'),
  }
}

export async function auditFinanceLedgerAi(
  token: string,
  body: { employeeEmailContains?: string; clientNameContains?: string; maxRows?: number },
): Promise<FinanceLedgerAuditResult> {
  const res = await fetch(`${base}/api/ai/finance/ledger-audit`, {
    method: 'POST',
    headers: authHeaders(token),
    body: JSON.stringify(body),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Ledger audit failed'))
  return assertFinanceLedgerAudit(await res.json())
}

export type FinanceQuoteDraftResult = {
  usedLlm: boolean
  llmNote: string | null
  suggestedTitle: string | null
  suggestedScopeSummary: string | null
  suggestedHours: number | null
  suggestedHourlyRate: number | null
  suggestedValidThroughYmd: string | null
  reviewerChecklist: string[]
}

function assertFinanceQuoteDraft(x: unknown): FinanceQuoteDraftResult {
  const r = x as Record<string, unknown>
  if (typeof r.usedLlm !== 'boolean' || !Array.isArray(r.reviewerChecklist))
    throw new Error('Invalid quote draft response')
  const ln = r.llmNote
  const sh = r.suggestedHours
  const sr = r.suggestedHourlyRate
  return {
    usedLlm: r.usedLlm,
    llmNote: typeof ln === 'string' ? ln : null,
    suggestedTitle: typeof r.suggestedTitle === 'string' ? r.suggestedTitle : null,
    suggestedScopeSummary: typeof r.suggestedScopeSummary === 'string' ? r.suggestedScopeSummary : null,
    suggestedHours: typeof sh === 'number' ? sh : null,
    suggestedHourlyRate: typeof sr === 'number' ? sr : null,
    suggestedValidThroughYmd: typeof r.suggestedValidThroughYmd === 'string' ? r.suggestedValidThroughYmd : null,
    reviewerChecklist: r.reviewerChecklist.filter((q): q is string => typeof q === 'string'),
  }
}

export async function draftFinanceQuoteAi(
  token: string,
  body: { clientId: string; contextEmployeeEmail?: string },
): Promise<FinanceQuoteDraftResult> {
  const res = await fetch(`${base}/api/ai/finance/quote-draft`, {
    method: 'POST',
    headers: authHeaders(token),
    body: JSON.stringify(body),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Quote draft AI failed'))
  return assertFinanceQuoteDraft(await res.json())
}
