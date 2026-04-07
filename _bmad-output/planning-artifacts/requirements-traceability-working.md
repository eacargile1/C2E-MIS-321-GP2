# Requirements Traceability (Working)

## Purpose

This is a practical, implementation-focused traceability sheet for current build work.  
Sources:
- `_bmad-output/planning-artifacts/prd.md`
- `_bmad-output/planning-artifacts/client-requirements.md`

Status values:
- `Met` = implemented and usable now
- `Partial` = visible behavior exists but core pieces are missing
- `Not Yet` = not implemented

## Current UI/Backend Traceability

| Requirement | Source | Status | Current evidence in app/code |
|---|---|---|---|
| FR1 login (email/password) | PRD | Met | Login flow in `web/src/App.tsx`; JWT auth endpoints active in API |
| FR2/FR3 user admin & roles | PRD | Met | Admin Users page + user create/patch/deactivate API |
| FR4 RBAC enforcement | PRD | Partial | Enforced on implemented endpoints; full matrix pending unbuilt modules |
| FR5 weekly timesheet entry fields | PRD + client req Time Tracking | Partial | Weekly editor supports date/client/project/task/hours/billable/notes; no employee selector in UI (implicit self) |
| FR6 edit/delete own timesheet pre-invoice | PRD | Partial | Edit/update exists; delete behavior still constrained by upsert model |
| FR10 org-wide visibility for Admin/Finance | PRD | Not Yet | Org-wide endpoint is currently placeholder |
| FR15 create projects with client + budget | PRD + client req Projects Tab | Met | Projects API + UI supports create/edit/list and budget |
| FR32/FR35 client management + search/filter | PRD + client req Manage Tab | Met | Clients API/UI create/edit/search/filter |
| FR20 availability calendar for all roles | PRD + client req Resource Tracker Sheet 1 | Partial | New month availability grid now visible to all authenticated roles on Resource Tracker/Timesheet page; currently shows signed-in user’s timesheet-derived availability only |
| Resource Tracker “Excel-style” monthly view | Client req Part 2 | Partial | Dense month grid added with color legend/load states; employee-row matrix not yet implemented |
| Role visibility: all roles can view availability | PRD RBAC + client req | Met (for current view) | Timesheet page authorized for all roles, monthly grid visible for all signed-in users |

## What we just aligned during this session

1. Renamed timesheet UX language to **Resource Tracker / Timesheet** (client vocabulary alignment).
2. Added full-month, always-visible calendar grid with placeholders for empty days.
3. Added compact, spreadsheet-like styling and load-state color coding.
4. Preserved weekly editor workflow so current save/edit functionality still works.

## Priority Gaps to stay aligned with PRD + client requirements

1. **Org-wide resource matrix** (high): calendar should show multiple employees as rows (not only current user data).
2. **Needs sheet equivalent** (high): staffing needs board with role level, sales stage, skills, TBD/OPEN.
3. **PTO and assignment propagation** (high): auto color/status updates from PTO approval and project assignment events.
4. **Timesheet audit trail** (high): tamper-evident change history.
5. **Reports + invoices** (high): currently largely not implemented.

## Build Rule Going Forward

For each UI/API change, cite the requirement ID in commit notes/PR notes:
- Example: `FR20/ClientReq-RT-Sheet1: improve month grid readability`
- Example: `FR15: project budget edit validation`

This keeps grading/client reviews grounded in documented scope instead of subjective UI-only progress.
