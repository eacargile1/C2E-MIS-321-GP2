const base = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5028'

async function parseJsonResponse(res: Response, parseErrorMessage: string): Promise<unknown> {
  const text = await res.text()
  if (!text.trim()) throw new Error(parseErrorMessage)
  try {
    return JSON.parse(text) as unknown
  } catch {
    throw new Error(parseErrorMessage)
  }
}

export async function readApiErrorMessage(res: Response, fallback: string): Promise<string> {
  try {
    const text = await res.text()
    if (!text.trim()) return fallback
    const data = JSON.parse(text) as { message?: unknown }
    return typeof data.message === 'string' ? data.message : fallback
  } catch {
    return fallback
  }
}

export type MeProfile = {
  id: string
  email: string
  role: string
  isActive: boolean
}

export type UserRow = {
  id: string
  email: string
  role: string
  isActive: boolean
}

export async function login(email: string, password: string) {
  const res = await fetch(`${base}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
  })
  const data = (await parseJsonResponse(res, 'Sign-in failed')) as
    | { accessToken: string; tokenType: string; expiresInSeconds: number }
    | { message: string }
  if (!res.ok) {
    const msg = 'message' in data && typeof data.message === 'string' ? data.message : 'Sign-in failed'
    throw new Error(msg)
  }
  if (!('accessToken' in data) || typeof data.accessToken !== 'string') throw new Error('Sign-in failed')
  return data
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
  return {
    id: data.id,
    email: data.email,
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

export async function listUsers(token: string): Promise<UserRow[]> {
  const res = await fetch(`${base}/api/users`, { headers: { Authorization: `Bearer ${token}` } })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load users'))
  const data = (await res.json()) as unknown
  if (!Array.isArray(data)) throw new Error('Could not load users')
  return data.map((row) => {
    const r = row as Record<string, unknown>
    if (
      typeof r.id !== 'string' ||
      typeof r.email !== 'string' ||
      typeof r.role !== 'string' ||
      typeof r.isActive !== 'boolean'
    )
      throw new Error('Could not load users')
    return { id: r.id, email: r.email, role: r.role, isActive: r.isActive }
  })
}

export async function createUser(token: string, email: string, password: string): Promise<UserRow> {
  const res = await fetch(`${base}/api/users`, {
    method: 'POST',
    headers: authHeaders(token),
    body: JSON.stringify({ email, password }),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not create user'))
  const r = (await res.json()) as Record<string, unknown>
  if (
    typeof r.id !== 'string' ||
    typeof r.email !== 'string' ||
    typeof r.role !== 'string' ||
    typeof r.isActive !== 'boolean'
  )
    throw new Error('Could not create user')
  return { id: r.id, email: r.email, role: r.role, isActive: r.isActive }
}

export async function patchUser(
  token: string,
  id: string,
  body: { email?: string; password?: string; isActive?: boolean; role?: string },
): Promise<UserRow> {
  const res = await fetch(`${base}/api/users/${id}`, {
    method: 'PATCH',
    headers: authHeaders(token),
    body: JSON.stringify(body),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not update user'))
  const r = (await res.json()) as Record<string, unknown>
  if (
    typeof r.id !== 'string' ||
    typeof r.email !== 'string' ||
    typeof r.role !== 'string' ||
    typeof r.isActive !== 'boolean'
  )
    throw new Error('Could not update user')
  return { id: r.id, email: r.email, role: r.role, isActive: r.isActive }
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

function assertClient(x: unknown): ClientRow {
  const r = x as Record<string, unknown>
  if (typeof r.id !== 'string' || typeof r.name !== 'string' || typeof r.isActive !== 'boolean')
    throw new Error('Could not load client')
  const projects = r.projects
  if (!Array.isArray(projects)) throw new Error('Could not load client')
  return {
    id: r.id,
    name: r.name,
    contactName: typeof r.contactName === 'string' ? r.contactName : null,
    contactEmail: typeof r.contactEmail === 'string' ? r.contactEmail : null,
    contactPhone: typeof r.contactPhone === 'string' ? r.contactPhone : null,
    defaultBillingRate: typeof r.defaultBillingRate === 'number' ? r.defaultBillingRate : null,
    notes: typeof r.notes === 'string' ? r.notes : null,
    isActive: r.isActive,
    projects: projects.map((p) => {
      const o = p as Record<string, unknown>
      if (typeof o.id !== 'string' || typeof o.name !== 'string') throw new Error('Could not load client')
      return { id: o.id, name: o.name }
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
}

function assertProject(x: unknown): ProjectRow {
  const r = x as Record<string, unknown>
  if (
    typeof r.id !== 'string' ||
    typeof r.name !== 'string' ||
    typeof r.clientId !== 'string' ||
    typeof r.clientName !== 'string' ||
    typeof r.budgetAmount !== 'number' ||
    typeof r.isActive !== 'boolean'
  )
    throw new Error('Could not load project')
  return {
    id: r.id,
    name: r.name,
    clientId: r.clientId,
    clientName: r.clientName,
    budgetAmount: r.budgetAmount,
    isActive: r.isActive,
  }
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
  body: { name: string; clientId: string; budgetAmount: number },
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
  body: { name?: string; clientId?: string; budgetAmount?: number; isActive?: boolean },
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
  }
}

export async function listMyExpenses(token: string): Promise<ExpenseRow[]> {
  const res = await fetch(`${base}/api/expenses/mine`, { headers: { Authorization: `Bearer ${token}` } })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load expenses'))
  const data = (await res.json()) as unknown
  if (!Array.isArray(data)) throw new Error('Could not load expenses')
  return data.map(assertExpense)
}

export async function createExpense(
  token: string,
  body: { expenseDate: string; client: string; project: string; category: string; description: string; amount: number },
): Promise<ExpenseRow> {
  const res = await fetch(`${base}/api/expenses`, {
    method: 'POST',
    headers: authHeaders(token),
    body: JSON.stringify(body),
  })
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not create expense'))
  return assertExpense(await res.json())
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
  if (!res.ok) throw new Error(await readApiErrorMessage(res, 'Could not load resource tracker'))
  const data = (await res.json()) as unknown
  if (!Array.isArray(data)) throw new Error('Could not load resource tracker')
  return data.map((row) => {
    const r = row as Record<string, unknown>
    if (
      typeof r.userId !== 'string' ||
      typeof r.email !== 'string' ||
      typeof r.role !== 'string' ||
      !Array.isArray(r.days)
    )
      throw new Error('Could not load resource tracker')
    const days = r.days.map((d) => {
      const x = d as Record<string, unknown>
      if (
        typeof x.date !== 'string' ||
        typeof x.status !== 'string' ||
        typeof x.hours !== 'number' ||
        (x.status !== 'Available' && x.status !== 'SoftBooked' && x.status !== 'FullyBooked' && x.status !== 'PTO')
      )
        throw new Error('Could not load resource tracker')
      return { date: x.date, status: x.status, hours: x.hours } as ResourceTrackerDay
    })
    return { userId: r.userId, email: r.email, role: r.role, days }
  })
}
