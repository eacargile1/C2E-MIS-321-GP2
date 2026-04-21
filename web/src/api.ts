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
  return { id: r.id, email: r.email, displayName, role: r.role, isActive: r.isActive, managerUserId }
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
  opts?: { displayName?: string; managerUserId?: string | null; role?: string },
): Promise<UserRow> {
  const body: Record<string, unknown> = { email, password }
  if (opts?.displayName !== undefined && opts.displayName.trim() !== '')
    body.displayName = opts.displayName.trim()
  if (opts?.managerUserId !== undefined) body.managerUserId = opts.managerUserId
  if (opts?.role !== undefined && opts.role.trim() !== '') body.role = opts.role.trim()
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

export type PersonalDetailRow = {
  client: string
  project: string
  totalHours: number
  billableHours: number
  nonBillableHours: number
}

export type PersonalDetail = {
  from: string
  to: string
  rows: PersonalDetailRow[]
}

export async function getPersonalDetail(token: string, from: string, to: string): Promise<PersonalDetail> {
  const qs = new URLSearchParams({ from, to })
  const res = await fetch(`${base}/api/reports/personal-detail?${qs}`, {
    headers: { Authorization: `Bearer ${token}` },
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load detail report'))
  const r = (await res.json()) as Record<string, unknown>
  if (typeof r.from !== 'string' || typeof r.to !== 'string' || !Array.isArray(r.rows))
    throw new Error('Could not load detail report')
  const rows = r.rows.map((row) => {
    const x = row as Record<string, unknown>
    if (
      typeof x.client !== 'string' ||
      typeof x.project !== 'string' ||
      typeof x.totalHours !== 'number' ||
      typeof x.billableHours !== 'number' ||
      typeof x.nonBillableHours !== 'number'
    )
      throw new Error('Could not load detail report')
    return {
      client: x.client,
      project: x.project,
      totalHours: x.totalHours,
      billableHours: x.billableHours,
      nonBillableHours: x.nonBillableHours,
    }
  })
  return { from: r.from, to: r.to, rows }
}

export type TeamMemberRow = {
  userId: string
  email: string
  displayName: string
  role: string
  totalHours: number
  billableHours: number
  nonBillableHours: number
  timesheetLineCount: number
  expenseCount: number
  expensePendingTotal: number
  expenseApprovedTotal: number
}

export type TeamSummary = {
  from: string
  to: string
  rows: TeamMemberRow[]
}

export async function getTeamSummary(token: string, from: string, to: string): Promise<TeamSummary> {
  const qs = new URLSearchParams({ from, to })
  const res = await fetch(`${base}/api/reports/team-summary?${qs}`, {
    headers: { Authorization: `Bearer ${token}` },
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load team report'))
  const r = (await res.json()) as Record<string, unknown>
  if (typeof r.from !== 'string' || typeof r.to !== 'string' || !Array.isArray(r.rows))
    throw new Error('Could not load team report')
  const rows = r.rows.map((row) => {
    const x = row as Record<string, unknown>
    if (
      typeof x.userId !== 'string' ||
      typeof x.email !== 'string' ||
      typeof x.displayName !== 'string' ||
      typeof x.role !== 'string' ||
      typeof x.totalHours !== 'number' ||
      typeof x.billableHours !== 'number' ||
      typeof x.nonBillableHours !== 'number' ||
      typeof x.timesheetLineCount !== 'number' ||
      typeof x.expenseCount !== 'number' ||
      typeof x.expensePendingTotal !== 'number' ||
      typeof x.expenseApprovedTotal !== 'number'
    )
      throw new Error('Could not load team report')
    return {
      userId: x.userId,
      email: x.email,
      displayName: x.displayName,
      role: x.role,
      totalHours: x.totalHours,
      billableHours: x.billableHours,
      nonBillableHours: x.nonBillableHours,
      timesheetLineCount: x.timesheetLineCount,
      expenseCount: x.expenseCount,
      expensePendingTotal: x.expensePendingTotal,
      expenseApprovedTotal: x.expenseApprovedTotal,
    }
  })
  return { from: r.from, to: r.to, rows }
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
  return {
    id: r.id,
    name: r.name,
    contactName: typeof r.contactName === 'string' ? r.contactName : null,
    contactEmail: typeof r.contactEmail === 'string' ? r.contactEmail : null,
    contactPhone: typeof r.contactPhone === 'string' ? r.contactPhone : null,
    defaultBillingRate: rate,
    notes: typeof r.notes === 'string' ? r.notes : null,
    isActive: r.isActive,
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
  return {
    userId: r.userId,
    email: r.email,
    displayName: r.displayName,
    role: r.role,
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
  totalHours: number
  billableHours: number
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
  return {
    weekStart: r.weekStart,
    status: st,
    totalHours: r.totalHours,
    billableHours: r.billableHours,
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
