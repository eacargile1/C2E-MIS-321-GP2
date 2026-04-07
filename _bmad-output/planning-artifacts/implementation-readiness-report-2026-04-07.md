---
stepsCompleted:
  - step-01-document-discovery
  - step-02-prd-analysis
  - step-03-epic-coverage-validation
  - step-04-ux-alignment
  - step-05-epic-quality-review
  - step-06-final-assessment
project_name: C2E-MIS-321-GP2
assessment_date: '2026-04-07'
canonical_documents:
  prd: prd.md
  architecture: architecture.md
  epics: epics-and-stories.md
  ux: ux-design-specification.md
status: complete
---

# Implementation Readiness Assessment Report

**Date:** 2026-04-07  
**Project:** C2E-MIS-321-GP2

---

## Document Discovery

### Scope

Searched `_bmad-output/planning-artifacts` for PRD, Architecture, Epics, and UX (whole and sharded `*/index.md` patterns).

### PRD documents

| Kind | Path | Notes |
|------|------|--------|
| Whole (canonical) | `prd.md` | Use for IR |
| Supporting | `prd-validation-report.md` | Validation artifact |
| Rendered exports | `prd.html`, `prd.pdf`, `prd.png`, `prd.jpeg` | **Non-authoritative**; keep for stakeholders only |

**Sharded PRD:** None (`prd/` + `index.md` not present).

### Architecture documents

| Kind | Path | Notes |
|------|------|--------|
| Whole (canonical) | `architecture.md` | Use for IR |
| Rendered exports | `architecture.html`, `architecture.pdf`, `.png`, `.jpeg` | Non-authoritative |

**Sharded architecture:** None.

### Epics & stories

| Kind | Path |
|------|------|
| Whole (canonical) | `epics-and-stories.md` |

**Sharded epics:** None.

### UX documents

| Kind | Path | Notes |
|------|------|--------|
| Whole (canonical) | `ux-design-specification.md` | Use for IR |
| Supporting | `ux-design-directions.html`, `ux-design-specification.html` | Non-authoritative |

### Other planning artifacts (not required by IR workflow but present)

- `client-requirements.md`, `client-requirements.html`, etc.
- `sprint-plan.md`, `tech-constraints.md`

### Duplicate / conflict resolution

- **Markdown vs HTML/PDF:** Treat **`.md` as single source of truth** for PRD, architecture, UX.
- **No whole-vs-sharded duplication** detected for required doc types.

---

## PRD Analysis

### Functional Requirements (complete extraction)

- **FR1:** Employees can authenticate via the application login (email and password)
- **FR2:** Admins can create, edit, and deactivate user accounts
- **FR3:** Admins can assign and change roles (Admin / Manager / Finance / IC) for any user
- **FR4:** The system enforces role-based access control by assigned role; unauthorized access attempts for restricted capabilities return a denied response
- **FR5:** Employees can submit weekly timesheet entries: project, client, task description, date, hours, billable/non-billable flag, optional notes
- **FR6:** Employees can edit or delete their own timesheet entries prior to invoice generation
- **FR7:** Employees can log time directly from their individual project view
- **FR8:** The system records a tamper-evident audit trail of all timesheet entry changes: actor, timestamp, previous value
- **FR9:** Managers can view timesheet entries for employees on their teams
- **FR10:** Admins and Finance can view timesheet entries across all employees
- **FR11:** Employees can submit expense entries: date, amount, category, description, optional project/client linkage
- **FR12:** Employees can attach a receipt file to an expense submission
- **FR13:** Employees can log expenses directly from their individual project view
- **FR14:** Submitted expenses can be linked to projects and clients for budget and invoice tracking
- **FR15:** Admins and Managers can create projects linked to a client with a defined budget
- **FR16:** Admins and Managers can assign employees to projects
- **FR17:** All users can view their personally assigned projects in a dedicated project view
- **FR18:** Project views can display total hours logged, expenses submitted, and budget consumed for that project
- **FR19:** Admins and Managers can view full project details including all team contributions and budget status
- **FR20:** All users can view the availability calendar showing all employees' status (Fully Booked / Soft Booked / Available / PTO)
- **FR21:** The system automatically sets an employee's status to Soft Booked when assigned to a project
- **FR22:** The system automatically sets an employee's status to Fully Booked when their project SOW is confirmed
- **FR23:** The system automatically marks calendar days as PTO when a PTO request is approved
- **FR24:** The system detects PTO/assignment conflicts and surfaces them to the employee's manager
- **FR25:** Admins can manually override an employee's availability status
- **FR26:** Admins and Managers can create and manage staffing needs entries: client, role level, sales stage, dates, required skills, assigned employee (specific person / TBD / OPEN)
- **FR27:** On staffing need creation, the system can generate ranked employee recommendations by skill match, current availability, and historical utilization
- **FR28:** The system can fall back to availability-only ranking when an employee has fewer than 3 completed project assignments in historical data, making AI scoring unreliable
- **FR29:** Employees can submit PTO requests with dates and reason
- **FR30:** Admins and Managers can approve or reject PTO requests
- **FR31:** Approved PTO automatically triggers calendar updates and conflict detection
- **FR32:** Admins can create, edit, and deactivate client records: name, contact information, billing rate, notes
- **FR33:** All users can view client records and associated projects
- **FR34:** Client records can display related projects and summary billing information
- **FR35:** Users can search and filter the client list
- **FR36:** Employees can view personal reports: total hours, billable hours, non-billable hours
- **FR37:** Managers can view team-level reports: hours and contributions per employee across their clients and projects
- **FR38:** Admins and Finance can view org-wide reports across all employees, clients, and projects
- **FR39:** Reports can be filtered by employee, client, project, and date range
- **FR40:** Client-level reports can display total billable hours, billable amounts, and per-employee breakdowns
- **FR41:** Finance and Admins can generate client invoices aggregating billable hours and reimbursable expenses for a specified period
- **FR42:** Generated invoices are immutable — corrections require a new invoice
- **FR43:** Finance and Admins can export invoices in PDF and/or CSV format
- **FR44:** Invoice line items can be traced back to source timesheet entries and expense submissions
- **FR45:** Admins can execute a one-time import of Resource Tracker data from `.xlsx` at launch
- **FR46:** Admins can review and correct data quality issues post-migration

**Total FRs:** 46

### Non-Functional Requirements (extracted)

**Performance**

- Page load and navigation: ≤2 seconds under normal load
- Availability calendar render (all employees): ≤3 seconds
- Cross-module state propagation: ≤5 seconds from triggering event
- Report queries (up to 2 years historical data): ≤5 seconds
- Invoice generation: ≤10 seconds regardless of line item count

**Security**

- All data encrypted at rest and in transit (TLS 1.2+ for API communication)
- Authentication via application login; passwords stored only as strong one-way hashes (never plaintext)
- Billing rates restricted to Finance and Admin — enforced server-side, not UI-only
- Timesheet audit trail entries are write-once and tamper-evident
- Session tokens expire after 30 minutes of idle inactivity (configurable by admin); re-authentication required
- Expense receipt attachments scanned for malware before storage

**Reliability**

- 99.5% uptime during business hours (Mon–Fri, 7am–7pm)
- Timesheet and expense submissions durably persisted before client confirmation returned
- Failed event propagation auto-retried; surfaced to admins if unresolved after 3 attempts

**Scalability**

- 70–100 concurrent users at launch without performance degradation
- Architecture supports growth to 200+ users via horizontal scaling — no schema or service redesign required
- Report query response time remains ≤5 seconds for datasets spanning up to 5 years of accumulated data

**Integration**

- **Auth (launch):** Email + password; optional future SSO/OIDC can be added without changing FR intent for RBAC and sessions
- Excel import: `.xlsx` format; malformed rows handled gracefully with error reporting
- Invoice export: PDF and/or CSV
- QuickBooks (Growth): QuickBooks Online API

### Additional requirements / constraints (from PRD)

- Domain rules: calendar visibility vs billing-rate confidentiality; invoice immutability; audit trail expectations; optimistic locking for concurrent project/budget admin access
- MVP is **full integrated replacement** at launch — not a phased module rollout
- Innovation: event-driven availability propagation; AI staffing with graceful degradation

### PRD completeness assessment

PRD is **complete enough for traceability**: numbered FR1–FR46, explicit NFR section, RBAC matrix, and scope boundaries. Remaining risk is **delivery feasibility** (all modules day-one) versus **actual implementation trajectory** — see Epic Quality and Final Assessment.

---

## Epic Coverage Validation

### Epic FR coverage (from `epics-and-stories.md`)

Epic document claims **full mapping FR1–FR46** to stories S-E1-01 … S-E11-02 with an explicit coverage matrix. No FR numbers appear missing from that matrix.

### Coverage matrix (summary)

| FR | Primary story | Status |
|----|----------------|--------|
| FR1 | S-E1-01 | Covered |
| FR2 | S-E1-02 | Covered |
| FR3 | S-E1-03 | Covered |
| FR4 | S-E1-04 | Covered |
| FR5 | S-E2-01 | Covered |
| FR6 | S-E2-02 | Covered |
| FR7 | S-E2-03 | Covered |
| FR8 | S-E2-04 | Covered |
| FR9 | S-E2-05 | Covered |
| FR10 | S-E2-06 | Covered |
| FR11 | S-E3-01 | Covered |
| FR12 | S-E3-02 | Covered |
| FR13 | S-E3-03 | Covered |
| FR14 | S-E3-04 | Covered |
| FR15 | S-E4-01 | Covered |
| FR16 | S-E4-02 | Covered |
| FR17 | S-E4-03 | Covered |
| FR18 | S-E4-04 | Covered |
| FR19 | S-E4-05 | Covered |
| FR20 | S-E5-01 | Covered |
| FR21 | S-E5-02 | Covered |
| FR22 | S-E5-03 | Covered |
| FR23 | S-E7-03 | Covered |
| FR24 | S-E7-04 | Covered |
| FR25 | S-E5-04 | Covered |
| FR26 | S-E5-05 | Covered |
| FR27 | S-E6-01 | Covered |
| FR28 | S-E6-02 | Covered |
| FR29 | S-E7-01 | Covered |
| FR30 | S-E7-02 | Covered |
| FR31 | S-E7-03 | Covered |
| FR32 | S-E8-01 | Covered |
| FR33 | S-E8-02 | Covered |
| FR34 | S-E8-03 | Covered |
| FR35 | S-E8-04 | Covered |
| FR36 | S-E9-01 | Covered |
| FR37 | S-E9-02 | Covered |
| FR38 | S-E9-03 | Covered |
| FR39 | S-E9-04 | Covered |
| FR40 | S-E9-05 | Covered |
| FR41 | S-E10-01 | Covered |
| FR42 | S-E10-02 | Covered |
| FR43 | S-E10-03 | Covered |
| FR44 | S-E10-04 | Covered |
| FR45 | S-E11-01 | Covered |
| FR46 | S-E11-02 | Covered |

### Coverage statistics

| Metric | Value |
|--------|--------|
| Total PRD FRs | 46 |
| FRs with a primary story in epics doc | 46 |
| Coverage | **100%** |

### Missing FR coverage

**None** at the **epic/story mapping** layer.

---

## UX Alignment Assessment

### UX document status

**Found:** `ux-design-specification.md` (substantive: journeys, patterns, role-aware IA, accessibility notes).

### UX ↔ PRD alignment

- **Strong alignment** on core promises: daily/weekly flows, four roles, staffing + calendar as centerpiece, invoice confidence for Finance, admin/migration concerns.
- UX explicitly encodes PRD constraints (e.g. WCAG 2.2 AA, desktop web MVP, information-dense layouts).

### UX ↔ Architecture alignment (document vs document)

- UX assumes **TanStack Router + TanStack Query + Tailwind + shadcn/ui + Zustand** per `architecture.md`.
- UX performance targets (calendar render ≤3s, fast reports) assume **server state caching/polling** patterns that architecture assigns to TanStack Query.

### UX ↔ Current codebase alignment (brownfield reality check)

**Partial / at risk:**

- Current `web` is **React + Vite + react-router-dom** with minimal pages — **not** the documented TanStack stack.
- No evidence in repo yet of full **module navigation**, **availability calendar**, **staffing board**, or **role-default landing** from UX spec.
- **Conclusion:** UX is valid relative to PRD, but **implementation is not yet aligned with the architecture+UX contract**. Treat UX as **target state**; either update architecture to match deliberate simplifications **or** schedule stories to converge the SPA to the documented stack.

### Warnings

- **Design drift risk:** If the team continues building without reconciling stack choice, UX patterns (keyboard-first grid, module sidebar) will fragment across ad-hoc components.
- **Accessibility:** UX requires non-color-only status; any calendar/board work must enforce text/icon labels — catch this in component-level AC.

---

## Epic Quality Review

Assessment against “create-epics-and-stories” style principles (user value, independence, story quality, dependencies).

### What is strong

- **Traceability:** Every FR maps to a story; epic sequencing section is dependency-aware and matches architecture’s spine (auth → clients/projects → events → PTO → reporting → invoices → migration).
- **Outcomes:** Most epics state an outcome paragraph (user/business value), not only technical tasks.

### Violations and risks

#### Major — acceptance criteria uneven

- Many stories (especially E3–E5, E8–E9) rely on table rows **without** detailed AC like E1/E2/E6/E10/E11.
- **Risk:** Implementation readiness for dev agents is weaker; edge cases (RBAC, error states) will be invented late.

**Remediation:** Add Given/When/Then AC + negative paths per story (403/404, validation, idempotency where relevant).

#### Major — “system stories” density

- Multiple stories are written **As the system** (E2-04, E3-04, E5-02/E5-03, E7-03). That can be valid, but it often hides **UI/ops acceptance** (admin visibility of propagation failures per PRD/NFR).

**Remediation:** Split into **observable outcomes**: user-visible behavior + admin diagnostics + metrics/logs.

#### Major — architecture assumptions vs running code

- `architecture.md` prescribes **SQL Server**, **transactional outbox + Hangfire**, **RFC 7807**, **URL versioning `/api/v1/`**, **Clean Architecture starter**.
- Current API is a **simpler ASP.NET Core app** with **EF Core InMemory** for dev/tests and differs materially from the ADRs as implemented today.

**Remediation:** Either (a) update architecture doc to reflect intentional MVP scaffolding, or (b) add explicit stories for **database + outbox + error shape + API versioning** before claiming later FRs are implementable “for real”.

#### Minor — epic independence is directional, not provable from stories

- Sequencing says E5 parallel with E2/E3 “once projects exist” — fine, but stories don’t explicitly state **feature flags/stubs** for partial vertical slices.

**Remediation:** For each epic, define a **stub milestone** vs **done milestone** if parallel work is expected.

### Quality checklist (summary)

| Check | Result |
|-------|--------|
| Epics deliver user value | Mostly yes (some system-centric stories) |
| Epic independence | Directionally ok; depends on honest scaffolding |
| Story sizing | Mixed — many stories are epic-sized implicitly |
| Forward dependencies | Not explicit in story text beyond sequencing doc |
| Clear AC | **Weak outside a few epics** |
| FR traceability | **Strong** |

---

## Summary and Recommendations

### Overall readiness status

**NEEDS WORK**

**Reason (plain):** PRD ↔ Epics traceability is **excellent (100% FR coverage)**, but **planned architecture + UX** are **not yet reconciled** with the **current codebase**, and several **NFRs are nowhere near satisfied** in the running implementation (durability, outbox/retry story, invoice/receipt pipelines, report performance, etc.). That’s normal for brownfield early scaffolding — but it is **not** “ready to implement the full MVP as specified” without either updating the plan or executing foundational stories.

### Critical issues requiring immediate action

1. **Resolve architecture drift:** Decide whether the CleanArch + SQL Server + outbox + Hangfire path is still the commitment, or update `architecture.md` to match the lean API approach — **do not let both be “true” silently**.
2. **Close the UX stack gap:** Align `web` with documented React stack (TanStack Router/Query/Tailwind/shadcn) **or** revise UX/architecture to the simpler stack actually in use.
3. **Harden story AC:** Expand acceptance criteria for epics E3–E9 so BMM dev agents don’t improvise security and validation.

### Recommended next steps

1. **Hold a 30-minute alignment checkpoint:** Pick canonical technical baseline (starter parity vs current repo). Record decision in `architecture.md` (ADR-style addendum).
2. **Create “foundation sprint” stories explicitly:** relational DB + migrations, durable writes, standardized API errors, session timeout semantics vs PRD 30-min idle, audit trail table strategy.
3. **Then** run your next BMM build loop on **one vertical slice** that proves the spine: *project assignment → availability event → calendar read* (even stubbed reads) **or** pick the team’s agreed priority from E8→E4.

### Brownfield code snapshot (for traceability)

As of this assessment, the repo demonstrates solid **early** implementation in:

- Auth + RBAC testing (`C2E.Api.Tests`)
- Timesheet weekly persistence patterns with validation + isolation tests
- Minimal React shell for login/admin/timesheet routes

It does **not** yet implement the majority of FR5+ surface area beyond partial timesheet flows and stubs.

### Final note

This assessment identified **planning traceability strength** (0 missing FR mappings) and **delivery coherence gaps** (architecture/UX vs code, AC depth, NFR feasibility). Address **architecture drift + foundational infrastructure decisions** before scaling parallel feature work with BMM agents.

---

_Report generated: `_bmad-output/planning-artifacts/implementation-readiness-report-2026-04-07.md`_
