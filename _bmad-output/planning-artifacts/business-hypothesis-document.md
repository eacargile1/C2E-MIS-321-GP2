# Business Hypothesis Document — C2E Unified Operations Platform (MIS321-GP2)

**Purpose:** State what we believe must be true for the product to succeed, how we will **test** those beliefs (not just “build it”), and what would cause us to **pivot** vs **persevere** — aligned with entrepreneurship coursework: *create a user experience vision → identify critical assumptions → build an early version to validate → release and measure → pivot or persevere*.

**Sources:** `_bmad-output/planning-artifacts/prd.md` (success criteria, MVP), `_bmad-output/planning-artifacts/client-requirements.md`, and course notes on assumption-driven validation.

---

## 1. User experience vision (one paragraph)

Managers and ICs open **one** internal app: they see **projects they are allowed to see**, log **weekly** time with clear rules, submit **expenses** with receipts, and (for leadership) place **staffing** against real availability. Finance and admins can **run the business**—users, clients, quotes, invoices—without exporting operational data to shadow spreadsheets. The emotional payoff: *“I don’t reconcile two systems on Sunday night anymore.”*

---

## 2. Critical assumptions (hypotheses)

Each row is falsifiable.

| ID | Hypothesis | If false, what breaks? |
|----|--------------|------------------------|
| **H1** | **One system** can replace both legacy tools **without** employees reverting to Excel/Harvest for “real” work. | Adoption failure; dual-system drag continues. |
| **H2** | **RBAC + project scoping** is sufficient so ICs never see inappropriate financial detail while Finance/Admin can. | Trust / compliance incident or workarounds. |
| **H3** | **Assignment / PTO / timesheet propagation** to a shared view is understandable enough that managers trust it for staffing. | Calendar ignored; staffing stays offline. |
| **H4** | Weekly **timesheet + quarter-hour rules** match how the firm actually works (not too heavy, not too loose). | Bad data quality or user revolt. |
| **H5** | **Expense + approval** flows match manager/partner/finance reality (not only IC happy path). | Revenue leakage or approval gridlock. |
| **H6** | **Invoice / quote** flows are credible for Finance with data sourced from the app. | Finance keeps parallel books. |
| **H7** | **AI-assisted** ops/finance features add value **without** blocking work when the LLM is off or wrong; humans stay in the loop. | Feature distrusted or ignored. |

---

## 3. How we test (methods — not “survey only”)

Course emphasis: *don’t just build; validate assumptions with behavior and artifacts.*

| Assumption | Early test (MVP / pilot) | Signal we collect |
|------------|---------------------------|-------------------|
| H1 | **Concierge-style pilot:** one team lives in the app for 2–4 weeks; banned parallel “truth” spreadsheet for that team. | Do they ask for exports? Do timesheets complete **in app**? |
| H2 | **Role walkthroughs** with scripted users (IC, manager, finance, admin) on same client/project data. | Wrong visibility = defect; track # RBAC issues. |
| H3 | Manager **staffing decision exercise** using only the availability grid + assignments. | Time to decision; “I don’t believe this number” quotes. |
| H4 | Compare **median time to submit** week vs old process; watch validation errors. | PRD metric direction. |
| H5 | Run **3–5 real expense scenarios** (thresholds, receipts, approvals) end-to-end. | Rejection reasons; cycle time. |
| H6 | Finance **dry-run** invoice from seeded billable time + expenses. | Line-level mismatches vs expectation. |
| H7 | Toggle key off / simulate LLM failure; observe whether core flows still complete. | `UsedLlm=false` paths still usable; no blocking. |

*Note:* Surveys alone are weak for B2B internal tools; **observed behavior** (completion rates, errors, time-on-task) is primary.

---

## 4. MVP as the “minimum instrument” to falsify hypotheses

From **PRD MVP** (paraphrased): integrated timesheets, expenses, projects, availability, staffing, clients, reports, RBAC, invoices, login, AI staffing assist + graceful degradation, migration path, admin overrides.

**Interpretation for testing:** The MVP is not “every feature ever” — it is the **smallest integrated slice** that lets H1–H7 be exercised on realistic client/project data.

---

## 5. Measures (examples — tune with the client)

| Outcome | Example measure | Persevere threshold (illustrative) |
|---------|-----------------|-----------------------------------|
| Adoption | % of weekly timesheets submitted **only** in-app | ≥ target set with client (e.g. match or beat old completion) |
| Data trust | # of manual “calendar override” events / head / month | Trending down after training |
| Staffing | Manager-reported “bad assignment due to tool” incidents | → 0 in pilot window |
| Finance | Invoice draft matches expected totals on sample clients | Match on pilot set |
| AI assist | % of sessions where LLM path used vs heuristic-only | Any usage with no increase in support tickets |

---

## 6. Pivot vs persevere (decision rules)

**Persevere** if:

- Pilot team completes core weekly loops **in platform** without parallel systems.  
- RBAC incidents are rare and fixable as bugs, not architectural surprises.  
- Finance accepts at least one **end-to-end** invoice dry run from system data.

**Pivot** (examples — not mutually exclusive):

- **Pivot A — Phased domain:** If H1 fails, narrow first wave to *timesheet + projects only* for one division while keeping tracker for others (violates original “big bang” PRD — only if survival demands it).  
- **Pivot B — Deeper integration:** If H6 fails, prioritize export / accounting integration *before* more AI.  
- **Pivot C — AI scope:** If H7 fails, strip AI to purely deterministic checks until trust returns.

---

## 7. Traceability

| Artifact | Role |
|----------|------|
| `prd.md` | Success criteria + MVP = **hypothesis set translated into requirements**. |
| `implementation-readiness-report-*.md` | Checks whether the build is **ready to test** hypotheses, not just “feature complete”. |
| `ux-design-specification.md` | UX vision elaborated for screens and flows. |

---

*Document status: complete for course / TA submission. Update measures and thresholds with the instructor or client as the pilot runs.*
