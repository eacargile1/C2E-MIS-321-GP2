# Statement of Work (SOW)

**Project:** C2E — MIS321 GP2 (Internal Professional Services Operations Platform)  
**Document version:** 1.2  
**Effective date:** April 23, 2026  

---

## 1. Purpose

This Statement of Work defines the scope, deliverables, and acceptance criteria for design, implementation, and handoff of a **single-tenant internal web application** that consolidates time tracking, expense management, client and project administration, resource visibility, approvals, finance operations (quotes and invoicing), and optional AI-assisted workflows for a professional services organization. **Matt Loft** acted as the **project client** (stakeholder / product direction) for this MIS 321 deliverable.

The solution replaces fragmented tools (e.g., separate time/expense tooling and spreadsheet-based resource tracking) with one **authoritative system**: ASP.NET Core **REST API** + **React** SPA, backed by a **relational database** (MySQL in production; in-memory for automated tests).

---

## 2. Parties

| Role | Organization | Representative |
|------|----------------|------------------|
| **Client (project)** | Matt Loft | Matt Loft |
| **Vendor / Delivery team** | University of Alabama — MIS 321 Group Project 2 | Evan Cargile; Evan Patterson; Gareth Garner |

---

## 3. Objectives

1. Provide **role-based access** so Admin, Partner, Finance, Manager, and Individual Contributor (IC) users see only appropriate data and actions.  
2. Enable **weekly timesheet capture** with validation, **weekly sign-off / approval** workflows aligned to project and org hierarchy.  
3. Support **expense submission and approval** with manager/admin review paths and finance visibility where specified.  
4. Maintain a **resource / availability view** derived from logged hours (and related rules) for org-wide planning.  
5. Support **client and project** directory operations, **staffing assignments**, **PTO requests** and approvals, and **reporting** for personal and operational use.  
6. Provide **finance** capabilities including expense register visibility, **client quotes**, **issued invoices** (including project expense billing and per-user payout-style issuance where implemented), and **AI-assisted** narrative/draft/audit features **subject to configuration and API availability**.  
7. Ship with **automated API tests**, configuration for **cloud database** deployment, and **demo / seed** options for training and acceptance.

---

## 4. Scope of Work (In Scope)

### 4.1 Application architecture

- **Backend:** ASP.NET Core Web API, JWT authentication, EF Core, migrations, RBAC authorization policies.  
- **Frontend:** React (Vite) SPA with routed modules for home, timesheet, expenses, clients, projects, resource tracker, reports, finance, and administration.  
- **Data:** Relational schema with documented migrations; support for `DATABASE_URL` or connection string configuration.

### 4.2 Security and access control

- Email/password login, JWT session handling, active-user enforcement.  
- Role model: **Admin**, **Partner**, **Finance**, **Manager**, **IC** with documented permission boundaries on API routes and UI affordances.  
- **IC catalog access:** Clients (and project stubs where applicable) scoped to staffed/catalog-allowed work as implemented.  
- **Finance portfolio** indicators for finance users on client APIs where implemented (roster + project-assigned finance).

### 4.3 Time tracking and approvals

- Weekly timesheet **CRUD** with validation (e.g., quarter-hour increments, catalog alignment where enforced).  
- **Weekly submission** and **approval/rejection** with status model; **submission snapshots** for pending weeks to avoid ambiguity between “grid total” and “what was submitted.”  
- Approval routing logic delegated to dedicated **project/org routing** services (delivery manager, engagement partner, reporting partner, etc.) as implemented in code.  
- **AI-assisted “Review week”** (rules + optional OpenAI) via server-side endpoints; **no auto-write** from AI to persisted timesheet lines.

### 4.4 Expenses

- Expense entry, status workflow (pending / approved / rejected), and role-appropriate listing and approval queues.  
- Team / manager / admin visibility rules per implementation.  
- **AI-assisted “Review draft”** for expenses (rules + optional OpenAI); advisory only.

### 4.5 Clients, projects, assignments, tasks

- Client directory, search, admin patch/deactivate; partner **finance lead** on client create where required; billing rate visibility rules.  
- Projects linked to clients, budgets, delivery manager / engagement partner / assigned finance fields as implemented.  
- **Assignments** (client roster, project team), and **project tasks** per API surface.  
- **Staffing recommendations** service with deterministic behavior and optional OpenAI reranking using shared `AIRecommendations` configuration.

### 4.6 PTO

- PTO request lifecycle, approver resolution, and listing endpoints consistent with the product model.

### 4.7 Finance and billing artifacts

- Finance **expense ledger** and related insights/narratives where implemented.  
- **Quotes** (create/list) with role rules as implemented.  
- **Invoices:** issuance flows supported by the API (e.g., project approved expenses, per-user payouts) with print/download as implemented.  
- **Finance AI assistant** endpoints for ledger audit / quote draft assistance, **subject to** `AIRecommendations:Provider` and valid OpenAI credentials.

### 4.8 Operations AI (cross-cutting)

- Server-side OpenAI integration for operations and finance assistants; **shared configuration** (`AIRecommendations`).  
- Documented behavior: deterministic path always available; LLM path when provider and key allow.

### 4.9 Reporting

- Personal and operational report endpoints as implemented (e.g., summaries by date range).

### 4.10 Administration and quality

- **User administration** (admin): create/update users, roles, manager/partner linkage as implemented.  
- **Demo scenario** and optional **demo finance** / **dev data purge** configuration for non-production environments.  
- **Integration test suite** for API (e.g., auth, RBAC, clients, projects, timesheets, expenses, assignments, invoices, quotes as covered).

### 4.11 Documentation and handoff

- Repository **README** with run instructions, default dev accounts (where seeding applies), and deployment notes.  
- **AI operations** design note for demos and compliance narrative.  
- This **Statement of Work** as the commercial scope anchor.

---

## 5. Out of Scope (Unless Added by Change Order)

- **Corporate SSO** (SAML/OIDC) at launch.  
- **Native mobile applications.**  
- **External client portal** with self-service billing.  
- **QuickBooks / ERP** automatic push of finalized invoices.  
- **Receipt OCR / vision** on uploaded PDFs (structured-field AI only unless separately scoped).  
- **Production 24/7 NOC**, penetration test remediation beyond agreed security baseline, or **managed hosting** of Client infrastructure (unless separately purchased).  
- Any item explicitly listed under **Growth** or **Vision** in the PRD that is not already reflected in Section 4.

---

## 6. Assumptions and Client responsibilities

**Academic context:** This SOW is written for a **course project**. Semester acceptance is based on the **delivered repository**, runnable application, tests, and documentation as specified in §7–§8 (local dev, CI, and demo/UAT-style review). Items below that reference production infrastructure describe a **hypothetical future deployment** if the product were adopted operationally; they are **not** required for MIS 321 grading unless the instructor specifies otherwise.

1. **Infrastructure:** For a future operational deployment, the adopting organization would provide non-production and production **MySQL** (or agreed DB), TLS, DNS, TLS certificates where needed, and runtime hosting for API and SPA (or agreed PaaS).  
2. **Secrets:** Client manages **OpenAI API keys**, JWT signing keys, and database credentials in secure configuration stores—not committed to source control.  
3. **Identity:** Initial user population and role assignment are performed with Admin tools or agreed import; Client validates **org chart** mapping (manager/partner assignments).  
4. **Data migration:** One-time import of legacy Resource Tracker / time system data is **out of scope** unless a separate migration SOW is executed; demo seed data is for **validation only**.  
5. **Browser support:** Current evergreen Chromium-based browsers and recent Safari/Firefox unless otherwise agreed.

---

## 7. Deliverables

| # | Deliverable | Format |
|---|-------------|--------|
| D1 | Source code repository (API, web, tests, migrations) | Git |
| D2 | Deployable API artifact / container instructions | As per README + CI |
| D3 | Built SPA static assets or dev/build pipeline | Vite/npm |
| D4 | Database migration scripts (EF Core) | Migrations in repo |
| D5 | Automated API test suite | `dotnet test` |
| D6 | Configuration templates (appsettings, env vars) | Repo + secure overrides |
| D7 | Operator documentation (runbook, AI config) | README + `docs/` |

---

## 8. Acceptance criteria (summary)

- All **in-scope** API routes return correct **401/403** for unauthenticated or unauthorized callers per RBAC matrix.  
- **Critical user journeys** (per PRD): IC weekly time and expenses; manager approvals; finance invoice/quote paths **as implemented** complete without unhandled errors in acceptance environment.  
- **AI:** With `Provider` set to `deterministic` or without API key, application remains functional and returns **heuristic** guidance; with valid `openai`/`hybrid` + key, LLM-enriched responses return within configured timeouts under normal OpenAI availability.  
- **Test suite** passes in CI or on a clean checkout using documented commands.  
- **Demo scenario** (when enabled) produces consistent seeded clients/projects/users for stakeholder walkthroughs.

---

## 9. Schedule and milestones (template)

| Milestone | Target date | Notes |
|-----------|-------------|--------|
| M1 — Requirements lock | _TBD_ | PRD / SOW sign-off |
| M2 — UAT build | _TBD_ | Deployed to UAT URL |
| M3 — UAT sign-off | _TBD_ | Acceptance checklist complete |
| M4 — Production go-live | _TBD_ | Cutover plan executed |

*(Target dates left TBD per course timeline; adjust if the instructor publishes fixed milestones.)*

---

## 10. Change control

Any work not described in Section 4 requires a **written change order** (scope, cost, schedule impact) agreed by both parties before implementation.

---

## 11. Fees and payment

This is an **academic course deliverable** under MIS 321. **No fees, deposits, or monetary consideration** apply between the parties for the work described in this SOW. Optional third-party costs (e.g., personal OpenAI API usage for demos) are borne by whoever incurs them and are outside this agreement.

---

## 12. Client sponsor and project team

**Project client (stakeholder):** Matt Loft  

**Teammates — University of Alabama, MIS 321 Group Project 2:**  

- Evan Cargile  
- Evan Patterson  
- Gareth Garner  

_No formal signature block is used for this course submission; the roster above identifies the delivery team and project client._

---

*This SOW summarizes the product as reflected in repository documentation and implementation as of document generation; authoritative feature behavior remains the shipped codebase and agreed PRD.*
