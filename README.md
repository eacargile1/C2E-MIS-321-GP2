# C2E — MIS321 GP2

Internal ops platform (professional services): consolidated time, clients, projects, expenses, and org-wide resource visibility. Specs live under `_bmad-output/planning-artifacts/` (PRD, epics, traceability).

## What’s implemented (current `main`)

- **Authentication** — Email/password login, JWT sessions, active-user gate.
- **Roles & RBAC** — Admin (full admin), then least→most operational access **IC → Manager → Partner → Finance** on shared delivery surfaces. IC cannot create clients or projects (browse only); **Partner, Finance, and Admin** may create clients; **Manager, Partner, Finance, and Admin** may create/patch projects. **Manager and Admin** see full expense line detail for their scope (`GET /api/expenses/team`: managers = direct reports; admins = org-wide). Expense approvals remain Manager+Admin; Finance ledger remains Finance+Admin.
- **Clients** — Directory, search; Admin create/edit/deactivate; billing rate visible to Admin + Finance only.
- **Projects** — List/filter; Admin + Manager create and patch; link to client and budget.
- **Timesheets** — Weekly entry (own lines only), upsert validation (quarter-hour hours).
- **Resource tracker** — Org-wide monthly grid derived from logged hours (all authenticated roles); weekly timesheet is a separate page.
- **Expenses** — Submit expenses (pending); Admin + Manager approve or reject; list own expenses.
- **User admin** — Admin-only user CRUD and role assignment (`/admin/users`).
- **Data** — EF Core migrations (Pomelo MySQL / in-memory for tests); Heroku-style `DATABASE_URL` (`mysql://…` from the MySQL add-on) or `ConnectionStrings:DefaultConnection`; API integration tests.

## Run locally

**API** (from `api/`):

```bash
dotnet run
```

Default URL is typically `http://localhost:5028` (see launch settings / console output).

**Web** (from `web/`):

```bash
npm install
npm run dev
```

Open `http://localhost:5173`. Set `VITE_API_BASE_URL` if the API is not on the default.

**Local dev logins (empty database only)** — On first startup when there are no users, the API seeds **five** accounts sharing the same password as `Seed:DevUserPassword` in `api/appsettings.Development.json` (default `ChangeMe!1`):

| Role | Email (default seed) |
|------|----------------------|
| Admin | Value of `Seed:DevUserEmail` (default `dev@c2e.local`) |
| Finance | `finance.dev@c2e.local` |
| Manager | `manager.dev@c2e.local` |
| Partner | `partner.dev@c2e.local` |
| IC | `ic.dev@c2e.local` (manager set to dev manager for expense approvals) |

Override the admin email/password with `Seed:DevUserEmail` / `Seed:DevUserPassword`. If the admin email equals one of the fixed dev addresses above, that duplicate slot is skipped.

In **Development**, `Seed:EnsureDevRoleAccounts` (default `true` in `appsettings.Development.json`) also creates `finance.dev@…`, `manager.dev@…`, `partner.dev@…`, and `ic.dev@…` on startup **if they are missing**—so older databases that only had a single admin seed still get IC/Finance/Manager/Partner logins without wiping the DB.

**Database / deployment (Heroku MySQL)** — Set the add-on’s URL as `DATABASE_URL` (e.g. `mysql://user:pass@host:3306/dbname`). The API parses it into a MySQL connection string with TLS (`SslMode=Preferred`). Alternatively, omit `DATABASE_URL` and set `ConnectionStrings__DefaultConnection` to a full MySQL connection string. Tests and local default runs use **in-memory** EF when `Database:InMemoryName` is set or when neither env nor connection string is configured (`EnsureCreated`); relational startup uses `MigrateAsync`.

**Tests** (from `tests/C2E.Api.Tests/`):

```bash
dotnet test
```

## Repo layout

| Path | Purpose |
|------|---------|
| `api/` | ASP.NET Core API |
| `web/` | Vite + React SPA |
| `tests/C2E.Api.Tests/` | API tests |
| `_bmad-output/` | Planning artifacts, PRD alignment, project context |
