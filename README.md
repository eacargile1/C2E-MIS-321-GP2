# C2E — MIS321 GP2

Internal ops platform (professional services): consolidated time, clients, projects, expenses, and org-wide resource visibility. Specs live under `_bmad-output/planning-artifacts/` (PRD, epics, traceability).

## What’s implemented (current `main`)

- **Authentication** — Email/password login, JWT sessions, active-user gate.
- **Roles & RBAC** — Admin, Manager, Finance, Individual Contributor; server-enforced routes (clients, projects, billing rates, invoices stub, expenses approvals).
- **Clients** — Directory, search; Admin create/edit/deactivate; billing rate visible to Admin + Finance only.
- **Projects** — List/filter; Admin + Manager create and patch; link to client and budget.
- **Timesheets** — Weekly entry (own lines only), upsert validation (quarter-hour hours).
- **Resource tracker** — Org-wide monthly grid derived from logged hours (all authenticated roles); weekly editor on the same page.
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
