# Idea Generation Document — C2E Unified Operations Platform (MIS321-GP2)

**Purpose:** Capture how the product idea was generated and refined before detailed planning (per entrepreneurship coursework: *planning before the plan* — real exploration before a fixed business plan).

**Sources:** `_bmad-output/planning-artifacts/client-requirements.md`, `_bmad-output/planning-artifacts/prd.md`, and course framing on idea generation (5 Ws, SCAMPER, *removing assumptions*).

---

## 1. Context — what problem space are we in?

| Question | Answer |
|----------|--------|
| **Who** | A professional services firm (~70–100 employees, ~30 clients): ICs, managers, finance, admins. |
| **What** | Internal operations: time, expenses, staffing, projects, reporting, invoicing. |
| **Where** | Single-tenant internal platform (replacing two disconnected tools). |
| **When** | Full cutover at launch (no phased “half in Excel” rollout). |
| **Why** | Two systems force manual reconciliation → wrong staffing, stale availability, bad forecasts, billing friction. |

---

## 2. Starting pain (before “the product” was one sentence)

From **client requirements**:

- **System A (Harvest-like):** time, expenses, projects, reports, clients, invoices.  
- **System B (Excel Resource Tracker):** scheduling, staffing forecasts, staffing needs.  
- **Gap:** Nothing reliably connects “who is staffed” ↔ “who logged hours” ↔ “what we invoice” without human glue.

**One-line problem statement:** *Operational truth is fragmented; the firm pays for it in time, errors, and customer trust.*

---

## 3. Idea generation techniques we applied

### 3.1 Removing assumptions (worked example)

| Assumption | Challenge | Opportunity |
|------------|-----------|-------------|
| “We need two tools because scheduling is different from time entry.” | Why can’t one data model carry **assignment → availability → timesheet → invoice**? | One platform with **event propagation** (assignments, PTO, timesheets update the same calendar). |
| “Managers will always reconcile Excel before staffing.” | What if reconciliation is **the bug**, not the process? | Staffing decisions happen **inside** the system on live availability. |
| “Invoicing can stay a separate export step forever.” | What if invoice truth must come from **logged** billable time + approved expenses? | Invoice generation from the same ledger as operations. |

Stripping these assumptions is what elevated the idea from “another Harvest clone” to **unified internal operations**.

### 3.2 SCAMPER (short pass on the legacy split)

| Letter | Prompt | Resulting direction |
|--------|--------|---------------------|
| **S** Substitute | Replace Excel + second app with **one** authoritative app. | Single product surface. |
| **C** Combine | Merge resource tracker + time/expense. | Shared client/project model + assignments. |
| **A** Adapt | Borrow patterns from tools teams already know (weekly timesheet, expense form). | Familiar UX, new backbone. |
| **M** Magnify | Emphasize **accuracy** as the differentiator for a services firm. | PRD “what makes this special”: accuracy by design. |
| **P** Put to other uses | Use timesheet + assignment data for **forecasting**, not only payroll. | Utilization / capacity story. |
| **E** Eliminate | Eliminate cross-tool copy/paste and duplicate project lists. | One source of truth. |
| **R** Rearrange | Put **staffing board** and **timesheet** in one workflow order: plan → execute → bill. | Journey ordering in PRD. |

### 3.3 Brainstorming themes (condensed)

Themes that survived prioritization:

1. **Truth in one place** — assignments, PTO, and time hit the same availability view.  
2. **Role-appropriate power** — IC vs manager vs finance vs admin (RBAC).  
3. **Invoice integrity** — generated from operational facts, not side spreadsheets.  
4. **AI as assist, not autopilot** — staffing suggestions with graceful degradation (PRD MVP).

---

## 4. Idea statement (selected concept)

**Product idea:** A unified internal **Consulting-to-Execution (C2E)** operations platform that replaces the Harvest-like stack *and* the Excel Resource Tracker by synchronizing staffing, time, expenses, budgets, and invoicing in one relational system—with explicit roles, audit-friendly expense flows, and optional AI-assisted staffing recommendations.

---

## 5. What we explicitly did *not* chase (scope discipline)

- Not a client-facing portal in MVP (vision phase).  
- Not QuickBooks sync at launch (growth).  
- Not “phased rollout that keeps Excel alive” — contradicts the core thesis.

---

## 6. Traceability to formal planning

| Artifact | How this doc feeds it |
|----------|------------------------|
| `prd.md` | Executive summary, MVP scope, and success criteria are the **crystallized** output of this ideation. |
| `client-requirements.md` | Structured checklist the client gave; ideation **explained why** one system beats two. |
| `business-hypothesis-document.md` | Next step: state **testable** beliefs so implementation can validate or pivot. |

---

*Document status: complete for course / TA submission. Align with `prd.md` if scope changes.*
