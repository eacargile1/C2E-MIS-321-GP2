# April 7, 2026 — Project update & runbook

**Purpose:** This is a single reference for the April 7 integration push: what shipped, how to run and use it, what’s solid vs. stubbed, and how that compares to the PRD. It’s written so anyone joining mid-stream can orient without digging through the whole commit history. (Implementation included paired engineering plus AI-assisted coding and refactors.)

**How to use this doc:** Read the TL;DR first, then run the app using **Section 4**, then walk **Section 5** while logged in under different roles if you can. Skim **Section 8** for the backlog and honest gaps vs. the PRD.

---

## 1. TL;DR — What happened today / this push

We turned the repository from a **planning-first / thin API** state into a **credible vertical slice of the internal “C2E” platform**: real database migrations, a coherent React shell, multiple modules wired to the API, role-based security enforced on the server, and automated API tests. The GitHub **`main`** branch was updated to include this full implementation so the repo reflects “the real project,” not just documents.

Rough scale (order of magnitude — counts shift as files move):

- **Tens of thousands of lines** added across API, web UI, tests, EF migrations, and planning/traceability artifacts.
- **40+ application files** touched or added in a single integration merge (API + web + tests + migrations + `_bmad-output` updates).
- **End-to-end workflows** you can demo: login → home dashboard → clients/projects directories → weekly timesheet → org “resource tracker” month grid → expenses submit → manager/admin approve expenses → admin user management.

You can go **end-to-end in one sitting**: run two processes, log in, and see Harvest-like concepts (time, expenses, clients, projects) in one place.

---

## 2. Project intent (why this repo exists)

**Product:** A single internal app for a professional services firm, replacing fragmented tools (time/expense vs. Excel resource tracking). The canonical requirements live in:

- `_bmad-output/planning-artifacts/prd.md`

**Tech stack (as implemented):**

| Layer | Stack |
|-------|--------|
| API | ASP.NET Core, EF Core, JWT auth |
| Database | SQLite for local/dev; design supports PostgreSQL / Heroku-style `DATABASE_URL` |
| Web | Vite + React + React Router |
| Tests | `Microsoft.AspNetCore.Mvc.Testing` integration tests |

---

## 3. Concrete deliverables — what was actually built

Below is a **functional** breakdown (not just file names). This is what you can truthfully say is “in the codebase.”

### 3.1 Authentication and identity

- Email + password login; JWT returned to the SPA; `/api/auth/me` exposes current user id, email, role, active flag.
- Middleware requires an **active** user account (deactivated users cannot use the API).

### 3.2 Role model and RBAC (Admin / Manager / Finance / IC)

Roles are enforced with **`[Authorize]`** and role name sets (see `api/Authorization/RbacRoleSets.cs`). Examples:

- **Client create / patch:** Admin only (matches PRD: only Admin edits clients).
- **Project create / patch:** Admin + Manager.
- **Client billing rates endpoint:** Admin + Finance (stub response today, but route is gated).
- **Invoice “generate” stub:** Admin + Finance.
- **Expense approval queue + approve/reject:** Admin + Manager (manager/admin approve PTO in PRD; we used the same pattern for expense workflow).

Important: Some reads (e.g. org-wide timesheet grid) are **`[Authorize]`** without extra roles so **every logged-in role** can see the matrix — intentional for the “everyone sees availability” PRD direction, with the caveat that cells are **hour-derived**, not full assignment/PTO propagation yet.

### 3.3 Clients module

- **API:** List (search `q`, optional inactive for Admin), get by id, create, patch. Billing rate on DTOs is suppressed for non–Admin/Finance viewers.
- **Web:** Clients page with directory table, admin create form, admin inline edit (name + active), search; Finance/Admin see rate column when API provides it.

### 3.4 Projects module

- **API:** List (filter by client, search, optional inactive), get by id, create, patch (Admin + Manager for writes).
- **Web:** Projects page with filters, create/edit for Admin+Manager, inactive toggle visibility for Admin.

### 3.5 Timesheets and “Resource Tracker”

- **Weekly timesheet:** `GET/PUT /api/timesheets/week?weekStart=YYYY-MM-DD` (Monday). Users can only upsert **their own** week; validation includes quarter-hour increments and hour caps.
- **Org month view:** `GET /api/timesheets/organization?monthStart=YYYY-MM-DD` returns all **active** users with per-day status derived from **sum of timesheet hours** (Available / SoftBooked / FullyBooked thresholds; PTO placeholder in shape but not fed by data yet).
- **Web:** Single **Resource Tracker / Timesheet** route (`/timesheet`): scrollable month matrix + weekly grid editor underneath.

### 3.6 Expenses module (major new user-visible feature)

Aligned with client screenshots (Harvest-style “submit for approval”):

- **IC (and any authenticated user):** `POST /api/expenses` with date, client, project, category, description, amount → row stored as **Pending**.
- **Self-service list:** `GET /api/expenses/mine`.
- **Managers + Admins:** `GET /api/expenses/approvals/pending`, `POST .../approve`, `POST .../reject`.
- **Web:** `/expenses` — form to submit, table of my expenses, and for Manager/Admin a pending queue with Approve/Reject.

**Not in v1 of this module:** receipt file upload, week batching UI like Harvest, “resubmit” flow, or finance-specific views — those are backlog (see Section 8).

### 3.7 Admin user management

- **API:** `/api/users` CRUD-style operations (Admin-only controller).
- **Web:** `/admin/users` — Admin only route in the SPA; create users, patch role/password/active.

### 3.8 Database and migrations

EF Core migrations were added and evolve the schema through:

- Initial/user baseline (as present in merged history)
- Clients
- Projects (client relationship, budget, etc.)
- **Expense workflow** table

After pulling `main`, run the API once (or `dotnet ef database update`) so your local DB matches migrations — see Section 4.

### 3.9 Automated tests

Under `tests/C2E.Api.Tests/`:

- Auth, clients, projects, timesheets, RBAC enforcement, expenses happy-path and permission tests, Heroku URL parsing tests, etc.

**Run:** `cd tests/C2E.Api.Tests` → `dotnet test` — expect all green.

### 3.10 UX / shell

- Light, corporate-style layout: top tabs (Home, Resource Tracker, Expenses, Clients, Projects, User Management for admins), dashboard KPIs, quick actions, optional compact density toggle.
- `web/src/App.css` and `web/src/index.css` carry most layout tokens.

### 3.11 Planning / traceability artifacts

Updates under `_bmad-output/` include things like:

- Epics/stories adjustments  
- Sprint/status plain-English notes  
- Requirements traceability working notes  
- Implementation readiness / project context where added in merge  

These help course stakeholders see **requirements ↔ code** without digging only in Git history.

### 3.12 Repository hygiene

- Root **`README.md`** summarizes “what’s implemented” and how to run.
- **`main`** on GitHub was fast-forwarded to include the integration work so the default branch is the full application, not docs-only.

---

## 4. How to run everything (step-by-step)

### 4.1 Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- Node.js 18+ (for Vite/React)

### 4.2 API

```powershell
cd api
dotnet run
```

Note the listening URL in the console (commonly `http://localhost:5028`). JWT signing key and DB options come from `appsettings` / environment — follow any README in `api/` if your machine needs user secrets.

First run applies migrations (depending on configuration).

### 4.3 Web app

```powershell
cd web
npm install
npm run dev -- --port 5173
```

Open **`http://localhost:5173`**.

If the API is not at `http://localhost:5028`, set:

```text
VITE_API_BASE_URL=http://localhost:YOUR_PORT
```

(CORS in the API defaults to allowing `http://localhost:5173` — using a different port without updating CORS will cause **failed to fetch**.)

### 4.4 Default / seeded admin

Tests and local setups often use seeded credentials from configuration, e.g. dev seed email/password in `Program.cs` / appsettings — **check `api/appsettings.Development.json` or your env** for the exact `Seed:DevUserEmail` and `Seed:DevUserPassword` on your machine. Create additional users from **User Management** once logged in as Admin.

### 4.5 Run tests

```powershell
cd tests/C2E.Api.Tests
dotnet test
```

---

## 5. How to navigate the app (click path guide)

After login, use the **top bar** (primary navigation).

### 5.1 Home (`/`)

- Shows welcome, role, and **three KPI cards:** active clients, active projects, **your** hours this week (from your timesheet API).
- **Quick actions:** shortcuts to timesheet, expenses, clients, projects; **Add User** tile only for Admin.
- **Workspace list:** same links.
- **Current delivery scope** blurb lists high-level capabilities.

### 5.2 Resource Tracker (`/timesheet`)

Two panels:

1. **Monthly availability grid** — pick month; see every employee × day; colors reflect hour totals (not true PTO/assignment engine yet). Clicking a cell jumps the weekly editor to that week.
2. **Weekly editor** — add lines: date (within week), client, project, task, hours, billable, notes → **Save** persists via API.

**Roles:** All roles see the org grid and can edit **only their own** weekly lines.

### 5.3 Expenses (`/expenses`)

- **Track expense** form → submits as **Pending**.
- **My expenses** table shows status (Pending / Approved / Rejected).
- **If Manager or Admin:** **Pending team approvals** with Approve / Reject.

**Roles:** Everyone submits; only Admin/Manager approve (Finance approves PTO in PRD; expense approval can be expanded to match finance rules later).

### 5.4 Clients (`/clients`)

- Search directory; Admin sees inactive toggle + create + edit; Finance/Admin see billing rate column when permitted.

### 5.5 Projects (`/projects`)

- List/filter; Admin + Manager can create and edit projects and budgets; Admin may see inactive filter depending on UI logic.

### 5.6 User management (`/admin/users`) — Admin only

- Create accounts, assign roles (Admin / Manager / Finance / IC), deactivate users.

If a non-admin hits the URL, they are redirected away.

---

## 6. API quick reference (for developers)

Base path pattern: `/api/...` (see each controller for exact routes).

| Area | Notable endpoints |
|------|-------------------|
| Auth | `POST /api/auth/login`, `GET /api/auth/me` |
| Users | `GET/POST /api/users`, `GET/PATCH /api/users/{id}` (Admin) |
| Clients | `GET /api/clients`, `GET /api/clients/{id}`, `POST`, `PATCH`, `GET /api/clients/billing-rates` (Admin+Finance stub) |
| Projects | `GET /api/projects`, `GET /api/projects/{id}`, `POST`, `PATCH` (writes: Admin+Manager) |
| Timesheets | `GET /api/timesheets/organization`, `GET/PUT /api/timesheets/week`, user-scoped week routes |
| Expenses | `GET /api/expenses/mine`, `POST /api/expenses`, `GET /api/expenses/approvals/pending`, `POST .../approve`, `POST .../reject` |
| Invoices | `POST /api/invoices/generate` (stub; Admin+Finance) |

Always send `Authorization: Bearer <token>` except login.

---

## 7. What is working vs. what the PRD still asks for

### 7.1 Working well (demo-ready)

- Login + JWT + RBAC on sensitive writes  
- Clients and projects as first-class entities with UI  
- Personal weekly timesheet persistence  
- Org-visible month grid (hour-based proxy for “availability”)  
- Expense submission + manager/admin approval loop  
- Admin user provisioning  
- Automated API regression tests  
- DB migrations for the above  

### 7.2 Partially aligned / simplified vs. PRD

- **Resource calendar semantics:** PRD wants assignment + PTO + SOW driving statuses. **Current:** hours logged drive status; PTO is structural placeholder.  
- **Timesheet lines:** Still use **text** client/project/task strings; PRD wants tight linkage to project entities and richer audit trail.  
- **Manager “my team”:** No formal org chart; expense approvals are **global** pending queue for managers, not “direct reports only.”  
- **Finance:** Billing rates endpoint exists but is stubby; invoices are stub; no QuickBooks.  
- **Reports module:** Not built (Harvest had Time / Detailed time / Detailed expense / Saved reports).  
- **Staffing board, AI staffing, Excel import, receipt uploads, PTO module, immutable invoice pipeline, tamper-evident audit** — not implemented or only stubbed.

### 7.3 Honest “client parity” statement

We’ve matched **categories** of their workflow (time, expenses, clients, projects, roles) but not **every Excel/Harvest UX detail**. The document you want for stakeholders is: **“We have an integrated internal app spine; next milestones close PRD gaps in reports, staffing, and finance-grade invoicing.”**

---

## 8. Backlog — what still needs to be done (prioritized)

Use this as a sprint planning list. Order is a suggestion.

1. **Reports** — Personal and filtered org/team summaries (billable vs non-billable, exports).  
2. **Project-scoped time/expense entry** — Pick project from dropdown; validate client/project ids; reduce free text.  
3. **Team model** — Manager sees only direct reports for approvals and timesheet review (PRD FR9-style).  
4. **PTO** — Request, approve, reflect on calendar, conflict detection (PRD FR29–FR31, FR24).  
5. **Staffing / assignments** — Needs board, SOW-driven Fully Booked, propagation (PRD FR21–FR26).  
6. **Expense receipts** — Upload, virus scan note in PRD, storage strategy.  
7. **Invoices** — Real aggregation from billable lines + approved expenses; PDF/CSV; immutability (PRD FR41–FR44).  
8. **Audit trail** — Timesheet change history (PRD FR8).  
9. **Seed data & demo script** — One-command demo dataset for class presentation.  
10. **CI** — GitHub Action: `dotnet test` + optionally `npm run build`.

---

## 9. Git / branch notes

- **`main`** on GitHub contains the full merge (this is the branch you should clone by default).  
- A named integration branch `eacargile1/mis321-work-2026-04-08` was used during development; history may show that name in PR discussions.  
- If anything looks missing locally: `git checkout main`, `git pull origin main`.

---

## 10. Troubleshooting

| Symptom | Likely cause | Fix |
|---------|----------------|-----|
| **Failed to fetch** from browser | API not running or wrong port / CORS | Run API; use `localhost:5173` for dev or add origin to CORS config |
| Empty grid / no users | DB empty or not migrated | Run API to migrate; create users as Admin |
| 403 on admin action | Logged in as IC/Finance | Use Admin or Manager where required |
| Tests fail after pull | Breaking change or missing SDK | `dotnet --version` should be 9.x; clean + rebuild |

---

## 11. Scope recap / standup framing

This push is a **large integration step**: from a documentation-heavy baseline to a **multi-module, RBAC-backed, tested, migratable** codebase with a demo-ready SPA. Work spanned **API + DB + UI + tests + planning/traceability docs**, with requirements grounded in the PRD and client workflow references.

**Navigation:** **Sections 4–5** are the runbook and UI tour; **Section 8** is the backlog and straight comparison to the PRD for roadmap discussions.

---

*Last updated April 7, 2026.*
