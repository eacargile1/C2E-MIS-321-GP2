---
stepsCompleted: ['step-01-init', 'step-02-discovery', 'step-02b-vision', 'step-02c-executive-summary', 'step-03-success', 'step-04-journeys', 'step-05-domain', 'step-06-innovation', 'step-07-project-type', 'step-08-scoping', 'step-09-functional', 'step-10-nonfunctional', 'step-11-polish', 'step-12-complete']
completedAt: '2026-03-05'
status: complete
classification:
  projectType: saas_b2b
  domain: general
  complexity: medium
  projectContext: brownfield
inputDocuments: ['_bmad-output/planning-artifacts/client-requirements.md', '_bmad-output/planning-artifacts/tech-constraints.md']
workflowType: 'prd'
---

# Product Requirements Document - MIS321-GP2

**Author:** Evanc
**Date:** 2026-03-05

---

## Executive Summary

MIS321-GP2 is a unified internal business operations platform for a professional services firm replacing two disconnected systems: a Harvest-style time/expense/invoicing tool and an Excel-based resource tracker. The platform consolidates staffing, time tracking, expense management, project budgeting, client management, reporting, and invoice generation into a single source of truth — eliminating manual cross-referencing that causes mis-scheduled employees, stale availability data, inaccurate forecasts, and billing inconsistencies.

**Target users:** Internal employees (Individual Contributors, Managers, Finance, Admins) at a services firm billing client work by the hour.

**Core problem solved:** Fragmented operational data forces managers to manually reconcile two tools before making staffing decisions, causes employees to show up with no work assigned due to inconsistent platform states, and makes forecasting unreliable because availability and actual hours logged are never in sync.

### What Makes This Special

The platform enforces data consistency by design. Staffing assignments, PTO approvals, and timesheet entries propagate automatically to the resource availability calendar — data is always current without manual updates. Managers make staffing decisions inside the platform on real availability, not stale data. Forecasts reflect actual expected billable hours. Invoices are generated directly from logged billable time and submitted expenses with no intermediate reconciliation step.

Core insight: for a services business, operational accuracy directly determines whether invoices go out correctly, whether people get paid, and whether the business can forecast its own capacity. This system makes accuracy the default.

**Project Classification:**
- **Type:** Single-tenant internal operations platform (B2B SaaS pattern)
- **Domain:** General business operations (services firm)
- **Complexity:** Medium — no regulatory overhead; non-trivial cross-module state synchronization
- **Context:** Brownfield replacement — consolidating two live systems at launch
- **Tech Stack:** JavaScript SPA (frontend), C# REST API (backend), Relational database

---

## Success Criteria

### User Success

- Every employee completes weekly timesheets entirely within the platform — no Excel touchpoints at launch
- Managers make project staffing decisions inside the platform with availability data reflecting real state at time of decision
- PTO approvals automatically reflect on the staffing calendar — no manual calendar updates required
- Individual contributors submit expenses and view their own hours/billing data without manager involvement

### Business Success

- **Full adoption at launch** — 100% of ~70–100 employees operating in the new system from day one; both legacy systems retired simultaneously
- **~8 hours/week recovered** per user currently spent on manual cross-referencing — ~560–800 person-hours/week across the org
- Staffing forecasts reflect actual availability, PTO, and assignments without manual reconciliation
- Invoices generated directly from logged billable hours and submitted expenses for all ~30 clients
- End-of-year Excel Resource Tracker data successfully imported at launch

### Technical Success

- 70–100 concurrent users supported without performance degradation
- Cross-module state propagation (assignment → calendar, PTO → calendar, timesheet → project budget) is real-time or near-real-time
- ~30 active clients with associated projects, timesheets, expenses, and invoices handled without data integrity issues
- RBAC enforced correctly across all four roles

### Measurable Outcomes

- Weekly timesheet submission time ≤ previous process time
- Zero employees showing up with no work assigned due to platform state inconsistency
- Availability calendar status matches actual assignments at any given time
- Invoice generation time reduced vs. current manual process

---

## Product Scope

### MVP — Minimum Viable Product

All modules must be fully functional and integrated at launch — this is a full system replacement, not a phased rollout. Both legacy systems retire on day one. A partial launch that leaves employees still needing Excel defeats the purpose.

**Must-Have Capabilities:**
1. Timesheet module — weekly entry per employee with billable/non-billable flag, linked to project and client
2. Expense tracking — submission form with receipt upload, linked to project/client
3. Availability calendar — color-coded (Fully Booked / Soft Booked / Available / PTO), auto-updating via event propagation
4. Project management — creation, team assignment, budget tracking, time/expense rollup
5. Individual project view (all roles) — every user sees assigned projects and can log time/expenses directly from their project view; dual-mode: Admin/Manager full management view + IC/Finance personal input view
6. Staffing needs board — by client, role, sales stage, skills, dates, assigned employee (or TBD/OPEN)
7. Client management — CRUD, contact info, related projects, search/filter
8. Reports — personal and client-level with filters (employee, client, project, date range)
9. Roles & permissions — Admin / Manager / Finance / IC with RBAC as defined
10. Invoice generation & export — from billable hours + reimbursable expenses per client; immutable once generated
11. SSO authentication — OAuth 2.0 / OIDC with company identity provider
12. AI-assisted staffing recommendations — ranked candidate suggestions on need creation based on skill match, availability, and historical utilization; degrades gracefully to availability-only ranking when data is insufficient
13. Excel data migration — one-time import of Resource Tracker end-of-year data at launch
14. Admin availability override — manual fallback for calendar status corrections

### Growth Features (Phase 2)

- Automated timesheet pre-population from project assignments (expected hours → draft entries editable per day)
- QuickBooks integration — push finalized invoices into accounting system via QuickBooks Online API
- Advanced forecasting dashboards — capacity planning views, utilization trends
- Notifications/alerts — PTO conflicts with assignments, upcoming staffing gaps, budget overruns

### Vision (Phase 3)

- Client portal — external client visibility into project status and invoices
- Mobile app — timesheet and expense submission on mobile devices

---

## User Journeys

### Journey 1 — Marcus, Individual Contributor (Weekly Routine)

Marcus is a mid-level developer at the firm. Every Friday he dreaded the 30-minute ritual: open Harvest, log hours from memory, cross-check Slack and calendar for billable items, then open the expense spreadsheet.

Now Marcus opens the platform. His project assignments are already there. He clicks into his timesheet — expected hours pre-populated from assignments. He adjusts Tuesday and Wednesday for overtime, adds a note, marks everything billable. He hits "Track Expense" for a software license, fills the form in under a minute, attaches the receipt, submits. Total time: 8 minutes.

**Capabilities revealed:** Timesheet entry, pre-populated hours from assignments, billable/non-billable flag, expense submission with receipt upload, project/client linkage.

---

### Journey 2 — Sarah, Manager (Monday Morning Staffing Decision)

Sarah manages four client accounts and a team of twelve. Monday mornings used to mean opening Harvest, the Excel Resource Tracker, and email to reconcile who was actually available before she could answer: "Can I put Jordan on Apex starting next week?"

Now Sarah opens the platform's staffing view. Jordan is Soft Booked through the 14th, then Available. The Apex needs entry requires a C# developer starting the 18th — Jordan fits. She assigns Jordan in-platform. Jordan's status automatically updates to Soft Booked for Apex; when the SOW closes, one click sets Fully Booked. She checks client reporting — Henderson is on track, Apex is under-paced. Flags it for the team meeting.

**Capabilities revealed:** Availability calendar, staffing needs board, in-platform assignment, auto-status propagation (Available → Soft Booked → Fully Booked), client-level reporting, manager team view.

---

### Journey 3 — Jordan, Individual Contributor (Edge Case — PTO + Assignment Conflict)

Jordan submits PTO for March 20–21. Request approved. Calendar automatically marks those days Yellow (PTO). The system detects Jordan is Soft Booked on Apex starting March 18th — flags the overlap in Sarah's manager view. Sarah reviews, determines the Apex start can slip two days, updates the assignment. Jordan's calendar reflects the adjusted start. No one shows up expecting 40 hours from Jordan that week.

**Capabilities revealed:** PTO request/approval, automatic calendar update on approval, conflict detection between PTO and assignments, manager alert, assignment date editing.

---

### Journey 4 — Diana, Finance (Invoice Run — End of Month)

End of month: Diana needs to invoice six clients. Previously: export Harvest data, manually match expenses, calculate totals, build invoices in Word. Two hours minimum, always at least one discrepancy.

Now Diana opens the finance view. Per client: all billable hours broken out by employee and project, plus reimbursable expenses. Henderson — 147 billable hours, 3 expenses totaling $840. She generates the invoice with one click, exports it, sends it to the client. Six invoices in 25 minutes.

**Capabilities revealed:** Finance invoice generation view, billable hours/expense aggregation by client, invoice generation and export, monthly billing cycle support, invoice immutability.

---

### Journey 5 — Alex, Admin (New Client + Project Setup)

Firm signs Vertex Analytics. Alex opens the Manage tab, creates the client record — name, contact info, billing rate. Creates the Vertex Q1 Engagement project, links it to the client, sets budget, assigns the team: Sarah as manager plus three ICs. Assignments propagate: availability calendars update, project appears in Sarah's manager view and each IC's project list. Done in under 10 minutes.

**Capabilities revealed:** Client creation, project creation, project-client linking, budget setting, team assignment, auto-propagation to dashboards and availability calendar.

---

### Journey Requirements Summary

| Capability Area | Revealed By |
|---|---|
| Timesheet entry + pre-population | Marcus, Jordan |
| Expense submission + receipt upload | Marcus |
| Availability calendar (color-coded, auto-updating) | Sarah, Jordan, Alex |
| Staffing needs board (client, role, sales stage) | Sarah |
| In-platform assignment + status propagation | Sarah, Alex |
| PTO request/approval + conflict detection | Jordan |
| Manager conflict alerts | Jordan |
| Client-level and personal reporting | Sarah, Diana |
| Invoice generation + export (Finance role) | Diana |
| Client management (create, edit, search) | Alex |
| Project creation, budget, team assignment | Alex |
| Role-based views across all four roles | All |

---

## Domain-Specific Requirements

### Data Privacy & Access Control

- Availability calendar (Fully Booked / Soft Booked / Available / PTO) is visible to all roles
- Employee billing rates are Finance and Admin only — enforced server-side
- Billable hours totals are visible to Managers (own team) and Finance; ICs see only their own
- Timesheet entries are individually scoped — employees enter and edit only their own contributions

### Invoice Data Integrity & Audit Trail

- Invoices are immutable once generated — corrections require issuing a new invoice
- Full audit trail on all timesheet entry changes prior to invoice generation: who changed what, when, and previous value
- Billable hours traceable from timesheet entry → project budget → invoice line item

### Concurrent Access

- Timesheet entries are individually owned — no concurrent editing conflicts expected
- Project/budget records use optimistic locking for admin/manager concurrent access

### Data Retention

- All timesheet, expense, invoice, and staffing data retained indefinitely
- Historical data queryable across any date range

---

## Innovation & Novel Patterns

### Event-Driven Availability Propagation

Availability status is never manually set as a standalone action — it is always the output of a real event (assignment created, PTO approved, SOW status changed). This eliminates the class of stale-data bugs inherent in multi-tool ops workflows. Every state change is event-sourced and propagates automatically to downstream modules.

### AI-Embedded Staffing Workflow

AI staffing recommendations are integrated directly into the assignment creation flow, not bolted on as a separate analytics layer. When a staffing need is created, the system proactively ranks candidates by skill match, current availability, and historical utilization — turning a manual search into a guided decision.

### Validation Approach

- Event propagation: all state-changing events update downstream availability within acceptable latency; conflict detection accuracy tested against known scenarios
- AI recommendations: validate relevance using migrated historical data; track manager acceptance rate as recommendation quality proxy

### Risk Mitigation

- If AI data is insufficient post-migration: degrade gracefully to availability-only ranking; the assignment workflow functions regardless
- If event propagation fails: auto-retry with admin visibility; manual override always available as safety valve

---

## Platform & Architecture Requirements

### Tenant Model

Single-tenant — one organization, one deployment. All data scoped to the organization; no multi-tenancy or tenant isolation required. User accounts represent internal employees only.

### Authentication & SSO

- SSO required at launch via company identity provider (Google Workspace, Azure AD, or Microsoft Entra — TBD)
- No standalone username/password auth; identity managed externally via SSO
- Role assignment (Admin / Manager / Finance / IC) managed within the platform post-SSO authentication
- Admin provisions new employee accounts after SSO onboarding

### RBAC Matrix

| Capability | Admin | Manager | Finance | IC |
|---|---|---|---|---|
| Submit own timesheet | ✓ | ✓ | ✓ | ✓ |
| Submit own expenses | ✓ | ✓ | ✓ | ✓ |
| Log time/expenses from project view | ✓ | ✓ | ✓ | ✓ |
| View own reports | ✓ | ✓ | ✓ | ✓ |
| View availability calendar (all employees) | ✓ | ✓ | ✓ | ✓ |
| View team billable hours | ✓ | ✓ (own team) | ✓ | ✗ |
| View employee billing rates | ✓ | ✗ | ✓ | ✗ |
| Assign employees to projects | ✓ | ✓ | ✗ | ✗ |
| Manage staffing needs board | ✓ | ✓ | ✗ | ✗ |
| Create/edit projects | ✓ | ✓ | ✗ | ✗ |
| Create/edit clients | ✓ | ✗ | ✗ | ✗ |
| Manage users & roles | ✓ | ✗ | ✗ | ✗ |
| Approve PTO requests | ✓ | ✓ | ✗ | ✗ |
| Generate & export invoices | ✓ | ✗ | ✓ | ✗ |
| Configure system settings | ✓ | ✗ | ✗ | ✗ |
| Override availability status | ✓ | ✗ | ✗ | ✗ |

### Integration List

**Launch:** SSO provider (OAuth 2.0 / OIDC); Excel `.xlsx` data import (one-time migration)

**Growth:** QuickBooks Online API — push finalized invoice data into accounting system

### Technical Architecture

- **Frontend:** JavaScript SPA (React or similar)
- **Backend:** C# REST API
- **Auth:** OAuth 2.0 / OIDC; JWT session tokens
- **State propagation:** Event-driven — assignment/PTO/SOW events trigger cross-module updates
- **Database:** Relational — supports complex joins across timesheets, projects, clients, employees, invoices

---

## Project Scoping & Risk

### MVP Philosophy

Platform MVP — all modules fully functional and integrated at launch. No phased user rollout; entire org migrates on day one. Modular build strategy enables independent component testing while requiring integrated release.

### Risk Mitigation

| Risk | Mitigation |
|---|---|
| Event propagation complexity | Build and validate the propagation pipeline first; all other modules depend on it |
| AI data quality at launch | Graceful degradation to availability-only ranking; improves over time |
| All-or-nothing launch constraint | Build modularly, test independently, release together |
| Excel migration data quality | Pre-import data review + admin correction tools post-migration |
| Behavior change at launch | Role-specific onboarding; admin tools to assist/correct initial entries |

---

## Functional Requirements

### Authentication & User Management

- **FR1:** Employees can authenticate via company SSO (OAuth 2.0 / OIDC)
- **FR2:** Admins can create, edit, and deactivate user accounts
- **FR3:** Admins can assign and change roles (Admin / Manager / Finance / IC) for any user
- **FR4:** The system enforces role-based access control by assigned role; unauthorized access attempts for restricted capabilities return a denied response

### Timesheet & Time Tracking

- **FR5:** Employees can submit weekly timesheet entries: project, client, task description, date, hours, billable/non-billable flag, optional notes
- **FR6:** Employees can edit or delete their own timesheet entries prior to invoice generation
- **FR7:** Employees can log time directly from their individual project view
- **FR8:** The system records a tamper-evident audit trail of all timesheet entry changes: actor, timestamp, previous value
- **FR9:** Managers can view timesheet entries for employees on their teams
- **FR10:** Admins and Finance can view timesheet entries across all employees

### Expense Tracking

- **FR11:** Employees can submit expense entries: date, amount, category, description, optional project/client linkage
- **FR12:** Employees can attach a receipt file to an expense submission
- **FR13:** Employees can log expenses directly from their individual project view
- **FR14:** Submitted expenses can be linked to projects and clients for budget and invoice tracking

### Project Management

- **FR15:** Admins and Managers can create projects linked to a client with a defined budget
- **FR16:** Admins and Managers can assign employees to projects
- **FR17:** All users can view their personally assigned projects in a dedicated project view
- **FR18:** Project views can display total hours logged, expenses submitted, and budget consumed for that project
- **FR19:** Admins and Managers can view full project details including all team contributions and budget status

### Staffing & Availability

- **FR20:** All users can view the availability calendar showing all employees' status (Fully Booked / Soft Booked / Available / PTO)
- **FR21:** The system automatically sets an employee's status to Soft Booked when assigned to a project
- **FR22:** The system automatically sets an employee's status to Fully Booked when their project SOW is confirmed
- **FR23:** The system automatically marks calendar days as PTO when a PTO request is approved
- **FR24:** The system detects PTO/assignment conflicts and surfaces them to the employee's manager
- **FR25:** Admins can manually override an employee's availability status
- **FR26:** Admins and Managers can create and manage staffing needs entries: client, role level, sales stage, dates, required skills, assigned employee (specific person / TBD / OPEN)

### AI-Assisted Staffing

- **FR27:** On staffing need creation, the system can generate ranked employee recommendations by skill match, current availability, and historical utilization
- **FR28:** The system can fall back to availability-only ranking when an employee has fewer than 3 completed project assignments in historical data, making AI scoring unreliable

### PTO Management

- **FR29:** Employees can submit PTO requests with dates and reason
- **FR30:** Admins and Managers can approve or reject PTO requests
- **FR31:** Approved PTO automatically triggers calendar updates and conflict detection

### Client Management

- **FR32:** Admins can create, edit, and deactivate client records: name, contact information, billing rate, notes
- **FR33:** All users can view client records and associated projects
- **FR34:** Client records can display related projects and summary billing information
- **FR35:** Users can search and filter the client list

### Reporting

- **FR36:** Employees can view personal reports: total hours, billable hours, non-billable hours
- **FR37:** Managers can view team-level reports: hours and contributions per employee across their clients and projects
- **FR38:** Admins and Finance can view org-wide reports across all employees, clients, and projects
- **FR39:** Reports can be filtered by employee, client, project, and date range
- **FR40:** Client-level reports can display total billable hours, billable amounts, and per-employee breakdowns

### Invoice Generation

- **FR41:** Finance and Admins can generate client invoices aggregating billable hours and reimbursable expenses for a specified period
- **FR42:** Generated invoices are immutable — corrections require a new invoice
- **FR43:** Finance and Admins can export invoices in PDF and/or CSV format
- **FR44:** Invoice line items can be traced back to source timesheet entries and expense submissions

### Data Migration

- **FR45:** Admins can execute a one-time import of Resource Tracker data from `.xlsx` at launch
- **FR46:** Admins can review and correct data quality issues post-migration

---

## Non-Functional Requirements

### Performance

- Page load and navigation: ≤2 seconds under normal load
- Availability calendar render (all employees): ≤3 seconds
- Cross-module state propagation: ≤5 seconds from triggering event
- Report queries (up to 2 years historical data): ≤5 seconds
- Invoice generation: ≤10 seconds regardless of line item count

### Security

- All data encrypted at rest and in transit (TLS 1.2+ for API communication)
- Authentication exclusively via SSO — no local credential storage
- Billing rates restricted to Finance and Admin — enforced server-side, not UI-only
- Timesheet audit trail entries are write-once and tamper-evident
- Session tokens expire after 30 minutes of idle inactivity (configurable by admin); re-authentication required
- Expense receipt attachments scanned for malware before storage

### Reliability

- 99.5% uptime during business hours (Mon–Fri, 7am–7pm)
- Timesheet and expense submissions durably persisted before client confirmation returned
- Failed event propagation auto-retried; surfaced to admins if unresolved after 3 attempts

### Scalability

- 70–100 concurrent users at launch without performance degradation
- Architecture supports growth to 200+ users via horizontal scaling — no schema or service redesign required
- Report query response time remains ≤5 seconds for datasets spanning up to 5 years of accumulated data

### Integration

- SSO: OAuth 2.0 / OIDC compatible with Google Workspace, Azure AD, and Microsoft Entra
- Excel import: `.xlsx` format; malformed rows handled gracefully with error reporting
- Invoice export: PDF and/or CSV
- QuickBooks (Growth): QuickBooks Online API
