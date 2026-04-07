# Sprint 1 Status (Plain English)

Last updated: 2026-04-07

This doc is intentionally simple and non-jargon.

## Goal
- Build a real foundation: login + users + clients + projects.

## What Was Built
- **Login + users:** auth, JWT, admin user management, role checks.
- **Clients:** real DB model, migrations, API (list/get/create/patch), search, frontend `/clients`.
- **Database path:** PostgreSQL + Heroku `DATABASE_URL` support, InMemory fallback for tests/local.
- **Projects:** real DB model + relation to clients, migration, API (`/api/projects` list/get/create/patch), frontend `/projects`.

## How It Works
1. User logs in and gets a token.
2. Frontend sends token to API on each request.
3. API enforces roles.
4. API stores and reads clients/projects from DB.
5. Frontend renders `/clients` and `/projects` from API responses.

## What Is Still Missing
- Project assignments to employees (FR16).
- Personal “my projects” behavior (FR17 depth).
- Project rollups (hours/expenses/budget consumed) (FR18).
- Full manager detail views (FR19 depth).

## Next Step (Immediate)
- Build **assignments** next:
  1) `ProjectAssignment` model + migration  
  2) assign/unassign API (Admin/Manager)  
  3) “my projects” endpoint for logged-in user  
  4) minimal UI for assignment management  
  5) tests for RBAC + ownership

## Weekly 5-Line Note Template
- **Goal:**  
- **Shipped:**  
- **Blocked by:**  
- **What I learned:**  
- **Next sprint top 3:**  

## Quick Start (How To Use What Works Right Now)

Think of this as a mini README for the current working features.

### 1) Start the app

Open two terminals from repo root:

Terminal A (API):
- `dotnet run --project api/C2E.Api.csproj`

Terminal B (Web):
- `cd web`
- `npm run dev`

Then open: `http://localhost:5173`

### 2) Login

Use the seeded dev account:
- **Email:** `dev@c2e.local`
- **Password:** `ChangeMe!1`

If login fails:
- Make sure API is running first.
- Check `api/appsettings.Development.json` has the same seed values.

### 3) Use Admin User Management (working)

From Home, click **Open user management**.

You can:
- create a user
- edit email/password/role
- deactivate/reactivate users

Important:
- New users default to `IC`.
- You cannot deactivate/demote the last active admin (guardrail is intentional).

### 4) Use Clients Page (working)

From Home, click **Clients**.

You can:
- search clients by name
- (Admin) create client
- (Admin) edit name + active status
- (Admin) include inactive clients in list

Role behavior:
- Admin + Finance can see billing rate.
- IC/Manager cannot see billing rate.

### 5) Use Projects Page (working)

From Home, click **Projects**.

You can:
- list projects
- filter by name/client
- (Admin/Manager) create project linked to a client
- (Admin/Manager) edit name + active status

Current limitation:
- Projects are not yet assigned to individual employees (that is next).

### 6) Timesheet (partially working)

From Home, click **Open timesheet**.

You can:
- view/save your week lines
- use week navigation

Current limitation:
- This is still pre-assignment integration (client/project values are text today).

### 7) One-command health check (optional)

From repo root:
- `dotnet test C2E.sln`
- `cd web && npm run build && npm run lint`

If these pass, the current implemented slice is healthy.
