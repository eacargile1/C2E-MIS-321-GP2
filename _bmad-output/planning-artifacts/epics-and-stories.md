---
workflowType: epics-and-stories
status: complete
derivedFrom: '_bmad-output/planning-artifacts/prd.md'
implementationAlignmentUpdated: '2026-04-07'
completedAt: '2026-04-01'
inputDocuments:
  - '_bmad-output/planning-artifacts/prd.md'
  - '_bmad-output/planning-artifacts/architecture.md'
---

# Epics & User Stories — MIS321-GP2

Backlog derived from PRD functional requirements (FR1–FR46). Story IDs are stable for traceability (`S-<epic>-<nn>`).

---

## Epic E1 — Authentication, login & RBAC

**Outcome:** Users sign in via the app login (email + password); admins provision users; RBAC is enforced on every protected capability.

| ID | User story | FRs |
|----|------------|-----|
| S-E1-01 | **As an** employee, **I want** to sign in through a normal app login with my email and password, **so that** I can use the platform without company SSO. | FR1 |
| S-E1-02 | **As an** admin, **I want** to create, edit, and deactivate user accounts, **so that** only active employees access the platform. | FR2 |
| S-E1-03 | **As an** admin, **I want** to assign and change roles (Admin / Manager / Finance / IC) for any user, **so that** permissions match job function. | FR3 |
| S-E1-04 | **As the** system, **I must** enforce RBAC on every restricted operation and return an explicit denial for unauthorized access, **so that** data access matches the RBAC matrix. | FR4 |

**S-E1-01 acceptance criteria**

- Login UI with email + password; safe handling of invalid credentials.
- Session established with JWT (or equivalent) suitable for API authorization.
- Passwords stored only as strong one-way hashes; TLS for API traffic outside local dev.

**S-E1-04 acceptance criteria**

- Spot-check denies: IC viewing org-wide timesheets, IC viewing billing rates, Finance creating clients, Manager accessing Finance-only invoice flows (per PRD matrix).
- Denials are consistent server-side (not UI-only hiding).

---

## Epic E2 — Timesheets, Audit Trail & Manager/Finance Visibility

**Outcome:** Weekly time entry, self-service edit/delete pre-invoice, full audit trail, and role-scoped visibility.

| ID | User story | FRs |
|----|------------|-----|
| S-E2-01 | **As an** employee, **I want** to submit weekly timesheet lines (project, client, task, date, hours, billable flag, notes), **so that** my work is capturable for billing and reporting. | FR5 |
| S-E2-02 | **As an** employee, **I want** to edit or delete my own timesheet entries until invoiced, **so that** I can correct mistakes before billing lock. | FR6 |
| S-E2-03 | **As an** employee, **I want** to log time from my project view, **so that** I don’t navigate away from context. | FR7 |
| S-E2-04 | **As the** system, **I must** record a tamper-evident audit trail for every timesheet change (actor, time, prior value), **so that** Finance can trust pre-invoice history. | FR8 |
| S-E2-05 | **As a** manager, **I want** to view timesheets for my team members, **so that** I can coach and validate effort. | FR9 |
| S-E2-06 | **As** admin or finance, **I want** to view timesheets across all employees, **so that** I can run billing and compliance checks. | FR10 |

**S-E2-04 acceptance criteria**

- Append-only or equivalent tamper-evidence; no silent overwrites of history.
- Audit records include before/after for numeric and foreign-key fields where applicable.

---

## Epic E3 — Expense Tracking

**Outcome:** Employees submit expenses with receipts and project linkage; data rolls up to projects and invoices.

| ID | User story | FRs |
|----|------------|-----|
| S-E3-01 | **As an** employee, **I want** to submit expenses (date, amount, category, description, optional project/client), **so that** reimbursable costs are captured. | FR11 |
| S-E3-02 | **As an** employee, **I want** to attach a receipt file to an expense, **so that** approvals and audits have evidence. | FR12 |
| S-E3-03 | **As an** employee, **I want** to log expenses from my project view, **so that** allocation is contextual. | FR13 |
| S-E3-04 | **As the** system, **I must** link submitted expenses to projects/clients for budget and invoice aggregation, **so that** FR14 and invoicing hold. | FR14 |

**S-E3-02 acceptance criteria**

- Receipts stored securely; malware scan before persistence (per NFR).
- File types/limits documented and enforced.

---

## Epic E4 — Projects, Assignments & Project Views

**Outcome:** Managers/admins manage projects and assignments; every user has a project view with rollups appropriate to role.

| ID | User story | FRs |
|----|------------|-----|
| S-E4-01 | **As** admin or manager, **I want** to create projects tied to a client with a budget, **so that** work is scoped and trackable. | FR15 |
| S-E4-02 | **As** admin or manager, **I want** to assign employees to projects, **so that** staffing is reflected in availability and personal views. | FR16 |
| S-E4-03 | **As any** user, **I want** a personal project view listing my assigned projects, **so that** I see my commitments in one place. | FR17 |
| S-E4-04 | **As any** user with project access, **I want** my project view to show hours logged, expenses, and budget consumption for that project, **so that** I understand burn. | FR18 |
| S-E4-05 | **As** admin or manager, **I want** full project detail including all team contributions and budget status, **so that** I can manage delivery. | FR19 |

---

## Epic E5 — Availability Calendar, Staffing Needs & Overrides

**Outcome:** Org-wide calendar reflects booking states and PTO; staffing needs board operational; admin override for edge cases.

| ID | User story | FRs |
|----|------------|-----|
| S-E5-01 | **As any** user, **I want** an availability calendar for all employees (Fully Booked / Soft Booked / Available / PTO), **so that** I can schedule realistically. | FR20 |
| S-E5-02 | **As the** system, **I must** set Soft Booked when an employee is assigned to a project, **so that** availability matches assignments. | FR21 |
| S-E5-03 | **As the** system, **I must** set Fully Booked when the project SOW is confirmed, **so that** committed capacity is visible. | FR22 |
| S-E5-04 | **As an** admin, **I want** to manually override an employee’s availability when data is wrong, **so that** operations can recover from edge cases. | FR25 |
| S-E5-05 | **As** admin or manager, **I want** to create and manage staffing needs (client, role level, sales stage, dates, skills, assignee or TBD/OPEN), **so that** demand is visible and assignable. | FR26 |

**S-E5-02 / S-E5-03 acceptance criteria**

- Propagation within PRD performance target (≤5s) under normal load.
- States are explainable from source events (assignment / SOW status), not free-typed.

---

## Epic E6 — AI-Assisted Staffing Recommendations

**Outcome:** Ranked candidates on need creation with graceful degradation.

| ID | User story | FRs |
|----|------------|-----|
| S-E6-01 | **As** admin or manager, **when** I create or open a staffing need, **I want** ranked employee recommendations (skills, availability, historical utilization), **so that** assignment decisions are faster and better informed. | FR27 |
| S-E6-02 | **As the** system, **when** historical data is insufficient (fewer than 3 completed assignments per employee per PRD FR28), **I want** to rank by availability only and label the limitation, **so that** the workflow never blocks on AI quality. | FR28 |

**S-E6-02 acceptance criteria**

- Threshold and UI copy match PRD (“Limited history — availability only” or equivalent).
- User can still assign without recommendations.

---

## Epic E7 — PTO & Assignment Conflicts

**Outcome:** PTO workflow feeds the calendar and warns managers on conflicts.

| ID | User story | FRs |
|----|------------|-----|
| S-E7-01 | **As an** employee, **I want** to submit PTO with dates and reason, **so that** my time away is requestable and auditable. | FR29 |
| S-E7-02 | **As** admin or manager, **I want** to approve or reject PTO requests, **so that** coverage is controlled. | FR30 |
| S-E7-03 | **As the** system, **on** PTO approval, **I must** update the calendar and run conflict detection against assignments, **so that** overlaps surface automatically. | FR23, FR31 |
| S-E7-04 | **As a** manager, **I want** to see when an employee’s PTO conflicts with assignments, **so that** I can reschedule work. | FR24 |

---

## Epic E8 — Client Directory

**Outcome:** Admins maintain clients; all users discover clients, projects, and summaries; search/filter works.

| ID | User story | FRs |
|----|------------|-----|
| S-E8-01 | **As an** admin, **I want** to create, edit, and deactivate clients (name, contacts, billing rate, notes), **so that** the org master is accurate. | FR32 |
| S-E8-02 | **As any** user, **I want** to view client records and related projects, **so that** I understand account structure. | FR33 |
| S-E8-03 | **As any** user, **I want** client detail to show related projects and summary billing info (within my visibility rules), **so that** context is unified. | FR34 |
| S-E8-04 | **As any** user, **I want** to search and filter the client list, **so that** I can find accounts quickly. | FR35 |

**Note:** Billing rate visibility remains Finance/Admin only per domain rules.

---

## Epic E9 — Reporting

**Outcome:** Personal, team, org-wide, and client-level reports with consistent filters.

| ID | User story | FRs |
|----|------------|-----|
| S-E9-01 | **As an** employee, **I want** personal reports (total, billable, non-billable hours), **so that** I track my utilization. | FR36 |
| S-E9-02 | **As a** manager, **I want** team reports across my clients/projects by employee, **so that** I can manage delivery. | FR37 |
| S-E9-03 | **As** admin or finance, **I want** org-wide reports across employees, clients, and projects, **so that** leadership and billing have one source of truth. | FR38 |
| S-E9-04 | **As** a report user (within my role), **I want** filters by employee, client, project, and date range, **so that** I can narrow analysis. | FR39 |
| S-E9-05 | **As** a report user with access, **I want** client-level reports with billable hours, amounts, and per-employee breakdown, **so that** account health is visible. | FR40 |

---

## Epic E10 — Invoice Generation & Traceability

**Outcome:** Finance (and admin) generate immutable invoices from hours + reimbursables, export, full traceability to sources.

| ID | User story | FRs |
|----|------------|-----|
| S-E10-01 | **As** finance or admin, **I want** to generate a client invoice for a period from billable hours and reimbursable expenses, **so that** billing matches logged work. | FR41 |
| S-E10-02 | **As the** system, **after** invoice generation, **I must** treat the invoice as immutable; corrections only via new invoice, **so that** audit and client trust hold. | FR42 |
| S-E10-03 | **As** finance or admin, **I want** to export invoices as PDF and/or CSV, **so that** delivery matches client and accounting needs. | FR43 |
| S-E10-04 | **As** finance or admin, **I want** every line item traceable to source timesheets and expenses, **so that** disputes are resolvable. | FR44 |

**S-E10-02 acceptance criteria**

- Post-generation edits to the same invoice record are blocked; adjustment workflow uses new document/version as defined by implementation.

---

## Epic E11 — Excel Migration & Data Quality

**Outcome:** One-time Resource Tracker import at launch with admin correction tools.

| ID | User story | FRs |
|----|------------|-----|
| S-E11-01 | **As an** admin, **I want** to run a one-time `.xlsx` import of Resource Tracker data at launch, **so that** historical staffing context seeds AI and reporting. | FR45 |
| S-E11-02 | **As an** admin, **I want** to review and fix rows that failed or look wrong after import, **so that** go-live data is trustworthy. | FR46 |

**S-E11-01 acceptance criteria**

- Malformed rows reported without failing entire batch (per NFR intent).
- Idempotency or clear re-run strategy documented for ops.

---

## Implementation alignment (repo `api/` + `web/` vs PRD)

**Authoritative requirements:** `prd.md` (FR1–FR46, NFRs). This section records **how much of each story is actually implemented today** so backlog work matches reality. Update this table when slices ship.

**Legend:** `Done` = meets story + FR intent for MVP depth · `Partial` = real behavior but missing major pieces · `Stub` = route/UI placeholder only · `Not started` = no substantive implementation

### E1 — Authentication, login & RBAC

| Story | Status | Notes |
|-------|--------|--------|
| S-E1-01 | **Done** | Email/password login, JWT, hashed passwords. NFR “30‑min idle timeout” is **not** idle sliding—token is **fixed expiry** from issue (`Jwt:AccessTokenMinutes`); align in a future story. |
| S-E1-02 | **Done** | List/create/patch users; deactivate; last-admin guards. |
| S-E1-03 | **Done** | Role patch with validation. |
| S-E1-04 | **Partial** | RBAC enforced on implemented routes + **stub** finance/admin routes; full matrix can’t be proven until all capabilities exist. |

### E2 — Timesheets, audit & visibility

| Story | Status | Notes |
|-------|--------|--------|
| S-E2-01 | **Partial** | Weekly lines (PUT/GET) with validation; **client/project are free text**, not linked entities (FR5 shape ok, **E4/E8 not in data model**). |
| S-E2-02 | **Partial** | Upsert **updates** lines; **no delete** path; **no invoice-generation lock** (FR6 “prior to invoice” not modeled). |
| S-E2-03 | **Not started** | No project view UI/API. |
| S-E2-04 | **Not started** | No tamper-evident audit trail (FR8). |
| S-E2-05 | **Not started** | No manager team-scoped timesheet read (FR9). |
| S-E2-06 | **Stub** | `GET /api/timesheets/organization` returns **empty** array; no org-wide data (FR10). |

### E3 — Expense tracking

| Story | Status | Notes |
|-------|--------|--------|
| S-E3-01 … S-E3-04 | **Not started** | No expense APIs, models, or UI (FR11–FR14). |

### E4 — Projects, assignments & project views

| Story | Status | Notes |
|-------|--------|--------|
| S-E4-01 | **Partial** | `Project` entity + migration + `POST /api/projects` (Admin/Manager), includes client linkage + budget (FR15). |
| S-E4-02 | **Not started** | Employee assignment to projects not implemented yet (FR16). |
| S-E4-03 | **Partial** | SPA `/projects` directory and list view exist; not yet user-assigned-personalized (FR17). |
| S-E4-04 | **Not started** | Project rollups for hours/expenses/budget consumed not implemented (FR18). |
| S-E4-05 | **Partial** | Admin/Manager can list/filter/patch projects, but no team contribution detail yet (FR19). |

### E5 — Availability, staffing needs & overrides

| Story | Status | Notes |
|-------|--------|--------|
| S-E5-01 … S-E5-05 | **Not started** | No calendar, staffing board, propagation, or override (FR20–FR22, FR25–FR26). FR21–FR22 depend on E4. |

### E6 — AI-assisted staffing

| Story | Status | Notes |
|-------|--------|--------|
| S-E6-01, S-E6-02 | **Not started** | No recommendation service (FR27–FR28). |

### E7 — PTO & conflicts

| Story | Status | Notes |
|-------|--------|--------|
| S-E7-01 … S-E7-04 | **Not started** | No PTO workflow (FR23–FR24, FR29–FR31). |

### E8 — Client directory

| Story | Status | Notes |
|-------|--------|--------|
| S-E8-01 | **Done** | Admin `POST`/`PATCH`, `Client` entity + migration; deactivate = `isActive` (FR32). |
| S-E8-02 | **Done** | Authenticated `GET /api/clients/{id}` (inactive hidden from non-admin). |
| S-E8-03 | **Partial** | Related `projects` now includes active project stubs; summary billing still pending (FR34). |
| S-E8-04 | **Done** | `GET /api/clients?q=` search + SPA `/clients` (FR35). |
| — | **Stub** | `GET /api/clients/billing-rates` still **IC employee rates** placeholder (not client rates). |

### E9 — Reporting

| Story | Status | Notes |
|-------|--------|--------|
| S-E9-01 … S-E9-05 | **Not started** | No reporting APIs or UI (FR36–FR40). |

### E10 — Invoices

| Story | Status | Notes |
|-------|--------|--------|
| S-E10-01 | **Stub** | `POST /api/invoices/generate` returns **204** with no invoice payload (FR41 not met). |
| S-E10-02 … S-E10-04 | **Not started** | Immutability, PDF/CSV export, traceability (FR42–FR44). |

### E11 — Excel migration

| Story | Status | Notes |
|-------|--------|--------|
| S-E11-01, S-E11-02 | **Not started** | No import pipeline (FR45–FR46). |

### Frontend (`web/`)

| Area | Status |
|------|--------|
| Login, in-memory session | **Done** |
| Admin users (list/create/patch) | **Done** (aligns with E1) |
| Weekly timesheet page | **Partial** (aligns with E2 partial) |
| Clients directory `/clients` | **Partial** (aligns with E8 — billing column only Admin/Finance) |
| Projects directory `/projects` | **Partial** (aligns with E4 — create/list/filter/edit active for Admin/Manager) |
| Remaining PRD modules (calendar, expenses, reports, invoices, migration, assignments) | **Not started** |

### Cross-cutting gaps (architecture / NFR vs current code)

- **Persistence:** **PostgreSQL path is implemented:** `DATABASE_URL` (Heroku) or `ConnectionStrings:DefaultConnection` + EF migrations (`api/Data/Migrations`), `MigrateAsync` on startup. **Tests and no-conn local runs** still use **InMemory**. Remaining gap vs NFRs: production hardening (backups, pooling, multi-dyno migration strategy) and filling out the rest of the relational schema beyond `Users` / `TimesheetLines`.
- **API shape:** Architecture cites RFC 7807 and `/api/v1/`; current API uses **ad-hoc JSON errors** and **unversioned** `/api/...`.
- **Event outbox / propagation:** Not present—required for E5/E7 and PRD MVP automation story.

**Implication:** Epics **E2–E11** remain the backlog; **E1** and **part of E2** are the only non-stub vertical slices today. Next build steps should follow PRD sequencing: **real persistence + E8 clients + E4 projects**, then deepen **E2** (audit, manager/org reads, delete/lock) before treating invoices or calendar as complete.

---

## Suggested implementation sequencing (dependency-aware)

1. **E1** — blocks everything secured.  
2. **E8** (clients) → **E4** (projects) — clients before projects.  
3. **E5** (calendar + needs + propagation) in parallel with **E2/E3** once projects exist — event pipeline early per architecture risk.  
4. **E7** (PTO) after calendar primitives.  
5. **E6** after migration + enough assignment history (can stub UI early).  
6. **E9** after time/expense data paths stable.  
7. **E10** after timesheets, expenses, and audit trail.  
8. **E11** early for test data; final cut at launch.

---

## Coverage matrix

| FR | Primary story |
|----|----------------|
| FR1 | S-E1-01 |
| FR2 | S-E1-02 |
| FR3 | S-E1-03 |
| FR4 | S-E1-04 |
| FR5 | S-E2-01 |
| FR6 | S-E2-02 |
| FR7 | S-E2-03 |
| FR8 | S-E2-04 |
| FR9 | S-E2-05 |
| FR10 | S-E2-06 |
| FR11 | S-E3-01 |
| FR12 | S-E3-02 |
| FR13 | S-E3-03 |
| FR14 | S-E3-04 |
| FR15 | S-E4-01 |
| FR16 | S-E4-02 |
| FR17 | S-E4-03 |
| FR18 | S-E4-04 |
| FR19 | S-E4-05 |
| FR20 | S-E5-01 |
| FR21 | S-E5-02 |
| FR22 | S-E5-03 |
| FR23 | S-E7-03 |
| FR24 | S-E7-04 |
| FR25 | S-E5-04 |
| FR26 | S-E5-05 |
| FR27 | S-E6-01 |
| FR28 | S-E6-02 |
| FR29 | S-E7-01 |
| FR30 | S-E7-02 |
| FR31 | S-E7-03 |
| FR32 | S-E8-01 |
| FR33 | S-E8-02 |
| FR34 | S-E8-03 |
| FR35 | S-E8-04 |
| FR36 | S-E9-01 |
| FR37 | S-E9-02 |
| FR38 | S-E9-03 |
| FR39 | S-E9-04 |
| FR40 | S-E9-05 |
| FR41 | S-E10-01 |
| FR42 | S-E10-02 |
| FR43 | S-E10-03 |
| FR44 | S-E10-04 |
| FR45 | S-E11-01 |
| FR46 | S-E11-02 |
