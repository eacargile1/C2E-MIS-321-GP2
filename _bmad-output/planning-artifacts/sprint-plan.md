---
workflowType: sprint-planning
status: draft
derivedFrom:
  - '_bmad-output/planning-artifacts/epics-and-stories.md'
  - '_bmad-output/planning-artifacts/architecture.md'
completedAt: '2026-04-01'
inputDocuments:
  - '_bmad-output/planning-artifacts/epics-and-stories.md'
  - '_bmad-output/planning-artifacts/prd.md'
---

# Sprint plan — MIS321-GP2

Assumptions: **2-week sprints**, one integrated increment per sprint, team velocity unknown — treat story lists as **targets**; pull fewer stories if velocity is lower. Story IDs reference `epics-and-stories.md`.

**Definition of Done (team baseline)**

- Merged to main, feature behind RBAC where applicable, basic happy-path + permission-denial tests for touched areas.
- No known P0 bugs for sprint-scope stories.
- Architecture risks called out in `architecture.md` (event pipeline, audit, auth) remain technically unblocked or explicitly deferred with a spike ticket.

---

## Sprint 1 — Identity & master data shell

**Goal:** App login works end-to-end; admins can manage users/roles; clients exist so projects can attach.

| Priority | Story ID | Notes |
|----------|-----------|--------|
| P0 | S-E1-01, S-E1-02, S-E1-03, S-E1-04 | E1 complete enough to gate APIs |
| P0 | S-E8-01, S-E8-04 | Admin client CRUD + search/filter |

**Stretch:** S-E8-02 read-only client view for non-admin roles (if RBAC for “any user” is ready).

**Exit criteria:** Authenticated session (email + password) on all environments; forbidden paths return consistent errors; at least one client record creatable and searchable.

---

## Sprint 2 — Projects, assignments, timesheet loop

**Goal:** Managers/admins run projects and staffing; ICs log weekly time from project context.

| Priority | Story ID | Notes |
|----------|-----------|--------|
| P0 | S-E4-01, S-E4-02, S-E4-03 | Projects + assignments + personal list |
| P0 | S-E2-01, S-E2-03 | Submit lines + log from project view |
| P0 | S-E2-02 | Edit/delete pre-invoice |

**Stretch:** S-E4-04 rollups if schema stable.

**Exit criteria:** Full path: client → project → assignment → timesheet line without manual SQL.

---

## Sprint 3 — Audit, expenses, calendar read model

**Goal:** Trustworthy pre-invoice history; expenses with receipts; org visibility of availability states.

| Priority | Story ID | Notes |
|----------|-----------|--------|
| P0 | S-E2-04 | Audit trail for timesheet changes |
| P0 | S-E3-01, S-E3-02, S-E3-03, S-E3-04 | Expenses + linkage |
| P0 | S-E5-01 | Calendar visibility |
| P0 | S-E2-05, S-E2-06 | Manager + admin/finance timesheet views |

**Exit criteria:** Audit records prove who changed what; expenses attach files; calendar shows states (may still be manual until S5-02/03).

---

## Sprint 4 — Availability automation, staffing needs, PTO

**Goal:** Assignment/SOW drives Soft/Fully Booked; staffing needs board; PTO workflow with conflict surfacing.

| Priority | Story ID | Notes |
|----------|-----------|--------|
| P0 | S-E5-02, S-E5-03, S-E5-04 | Propagation + admin override |
| P0 | S-E5-05 | Staffing needs CRUD |
| P0 | S-E7-01, S-E7-02, S-E7-03, S-E7-04 | PTO + calendar + conflicts |

**Risk / spike:** ≤5s propagation under load — perf test early.

**Exit criteria:** Explaining Soft vs Full from source events; manager sees PTO vs assignment conflicts.

---

## Sprint 5 — AI recommendations, reporting, client detail

**Goal:** Ranked staffing suggestions with degradation; role-scoped reports; client summaries.

| Priority | Story ID | Notes |
|----------|-----------|--------|
| P0 | S-E6-01, S-E6-02 | Recommendations + “limited history” path |
| P0 | S-E9-01 — S-E9-05 | Reporting + filters |
| P0 | S-E8-02, S-E8-03 | Client discovery + detail |

**Exit criteria:** IC blocked from Finance-only report paths; S-E6-02 never blocks assignment.

---

## Sprint 6 — Invoicing, migration, hardening

**Goal:** Finance generates immutable invoices with traceability; launch import; bug sweep.

| Priority | Story ID | Notes |
|----------|-----------|--------|
| P0 | S-E10-01, S-E10-02, S-E10-03, S-E10-04 | Full invoice epic |
| P0 | S-E11-01, S-E11-02 | Excel import + admin repair |
| P0 | S-E4-04, S-E4-05 | Close any remaining project rollup / detail gaps |

**Exit criteria:** Post-invoice timesheet lock behavior matches PRD; line items drill to sources; import errors are row-level and recoverable.

---

## Dependency quick reference

```text
E1 → all secured features
E8 → E4 → E2/E3/E5 (projects before time/expense/calendar signals)
E5 primitives → E7
E2+E3+audit stable → E10
E11 parallel early (dev/test); final run Sprint 6
```

---

## Capacity & trade-offs

- If **one sprint slips**, drop in order: Sprint 5 stretch (S-E9-05 depth first), then defer S-E6 polish (keep S-E6-02 degradation only), then reduce S-E5-05 UX depth.
- If **login slips**, cut scope to a minimal credential path with documented tech debt — keep hashed passwords and JWT session contract.
