---
title: 'ai-staffing-recommendations'
type: 'feature'
created: '2026-04-20'
baseline_commit: 'a664e4f6de1b5becf09a53b92d0cd1585fd1a5f1'
updated: '2026-04-20'
status: 'in-review'
context:
  - '_bmad-output/planning-artifacts/prd.md'
  - '_bmad-output/project-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Staffing assignment is currently manual and relies on users scanning availability and team history by hand, which slows assignments and causes inconsistent decisions. The PRD requires ranked AI-assisted recommendations with a graceful fallback when historical data is insufficient.

**Approach:** Add a recommendation pipeline to the staffing workflow that ranks employees by weighted signals (skills, current availability proxy, historical utilization proxy) and returns transparent score breakdowns. Keep the workflow deterministic and available by falling back to availability-only ranking when recommendation confidence is low or history is too sparse.

## Boundaries & Constraints

**Always:** Keep server-side RBAC with `RbacRoleSets`; keep recommendation endpoint restricted to Admin/Manager/Partner flows; return explicit rationale per suggestion; implement deterministic fallback when user has fewer than 3 completed assignments or scoring input is missing; preserve existing assignment CRUD behavior; add integration tests for role enforcement and fallback behavior.

**Ask First:** Introducing external LLM/provider APIs; changing DB schema for a full skills ontology beyond a minimal tag model; changing assignment UX flow beyond an additive recommendations panel.

**Never:** Never block assignment creation on AI service availability; never persist opaque model-only scores without explainable breakdown fields; never expose recommendation endpoints to IC/Finance roles.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| RECOMMENDATION_HAPPY_PATH | Admin/Manager/Partner requests recommendations for need with required skills and date window; candidates have history | API returns ranked candidates with total score + component scores (`skill`, `availability`, `utilization`) + rationale strings | N/A |
| SPARSE_HISTORY_FALLBACK | Candidate has `<3` completed assignments or missing utilization data | Candidate scored with availability-only strategy, response flags `fallbackMode: availability_only` | API does not fail; includes reason `insufficient_history` |
| NO_SKILL_DATA | Required skills supplied but no candidate has skill tags | Ranking still returned based on availability/utilization with skill score = 0 | API returns 200 with warning message in payload |
| UNAUTHORIZED_ROLE | IC/Finance token calls endpoint | Request denied | 403 forbidden JSON response via existing auth handler |
| SERVICE_OR_SCORING_FAILURE | Internal scoring throws or dependency unavailable | Endpoint returns deterministic availability-only ranking for all candidates | Log warning; return 200 fallback payload with `fallbackMode: system_fallback` |

</frozen-after-approval>

## Code Map

- `api/Controllers/AssignmentsController.cs` -- add recommendation endpoint for staffing workflows.
- `api/Dtos/AssignmentDtos.cs` -- define request/response DTOs for recommendation input and scored results.
- `api/Models/AppUser.cs` -- extend with minimal skill-tag field or related model navigation needed by scorer.
- `api/Models/ProjectEmployeeAssignment.cs` -- source of completed-assignment history for utilization signals.
- `api/Data/AppDbContext.cs` -- include new skill-tag entity configuration if normalized table is used.
- `api/Authorization/RbacRoleSets.cs` -- ensure role set constant covers recommendation endpoint authorization.
- `api/Services/StaffingRecommendationService.cs` -- new deterministic scoring service and fallback orchestration.
- `tests/C2E.Api.Tests/RbacEnforcementTests.cs` -- verify role gating for recommendation endpoint.
- `tests/C2E.Api.Tests/AssignmentsApiTests.cs` -- verify ranking output, fallback modes, and error resilience.
- `web/src/api.ts` -- add typed API client for recommendation request/response.
- `web/src/components/AssignmentManager.tsx` -- trigger recommendation lookup and display ranked list with rationale.
- `web/src/pages/Projects.tsx` -- wire recommendations into assignment creation/edit flow when staffing need context exists.
- `web/src/App.css` -- style recommendation list, score chips, and fallback badges consistently.

## Tasks & Acceptance

**Execution:**
- [x] `api/Dtos/AssignmentDtos.cs` -- add `StaffingRecommendationRequestDto`, `StaffingRecommendationResultDto`, and `StaffingRecommendationResponseDto` with explicit fallback metadata -- standardizes API contract and explainability.
- [x] `api/Services/StaffingRecommendationService.cs` -- implement weighted scoring (`skill`, `availability`, `utilization`) and deterministic fallback branches -- isolates recommendation logic from controllers.
- [x] `api/Controllers/AssignmentsController.cs` -- add authorized POST endpoint to compute recommendations from staffing need context -- exposes feature where assignment actions already live.
- [x] `api/Models/AppUser.cs` and `api/Data/AppDbContext.cs` -- use a temporary inference path (no schema migration) by deriving coarse skill signals from current user/profile data -- enables non-zero skill matching without DB churn in MVP.
- [x] `tests/C2E.Api.Tests/AssignmentsApiTests.cs` -- add integration tests for happy path ranking, sparse-history fallback, and scorer failure fallback -- prevents silent regression in core AI behavior.
- [x] `tests/C2E.Api.Tests/RbacEnforcementTests.cs` -- assert IC/Finance forbidden and Admin/Manager/Partner allowed on recommendation endpoint -- enforces PRD RBAC guarantees.
- [x] `web/src/api.ts` -- add `getStaffingRecommendations()` with runtime shape validation -- protects UI from malformed payloads.
- [x] `web/src/components/AssignmentManager.tsx` -- render "Recommend candidates" action and ranked results panel with score/rationale/fallback labels -- embeds AI directly in staffing workflow.
- [x] `web/src/App.css` -- add styles for recommendation cards and fallback status indicators -- keeps UX readable and consistent.

**Acceptance Criteria:**
- Given an Admin, Manager, or Partner creates a staffing need with required skills and dates, when they request recommendations, then the API returns a ranked list with per-candidate component scores and human-readable rationale.
- Given a candidate has fewer than 3 completed assignments, when recommendations are generated, then that candidate is scored with availability-only logic and marked with `insufficient_history` fallback metadata.
- Given utilization inputs are unavailable due to internal scoring failure, when recommendations are requested, then the endpoint still returns a deterministic availability-only ranking and does not block assignment flow.
- Given an IC or Finance user calls the recommendations endpoint, when authorization runs, then the response is forbidden and no recommendation payload is returned.
- Given recommendation payload fields are missing or invalid, when the frontend receives the response, then the UI rejects invalid data and surfaces a non-crashing error state.

## Spec Change Log

## Design Notes

Use an explicit weighted formula in `StaffingRecommendationService` to keep behavior testable:

```csharp
totalScore = (skillScore * 0.50m) + (availabilityScore * 0.30m) + (utilizationScore * 0.20m);
```

When fallback is required, force:

```csharp
totalScore = availabilityScore;
fallbackMode = "availability_only" | "system_fallback";
```

This preserves deterministic output and avoids hiding decision rules behind opaque model calls.

## Verification

**Commands:**
- `dotnet test tests/C2E.Api.Tests/C2E.Api.Tests.csproj` -- expected: all new recommendation and RBAC tests pass.
- `dotnet build api/C2E.Api.csproj` -- expected: API builds with new DTO/service/endpoint changes.
- `npm run lint --prefix web` -- expected: no TypeScript/React lint regressions in recommendation UI wiring.
- `dotnet test tests/C2E.Api.Tests/C2E.Api.Tests.csproj -p:UseAppHost=false` -- expected: test host boots and all API tests pass in current environment.
- `dotnet build api/C2E.Api.csproj -p:UseAppHost=false` -- expected: API builds successfully when apphost signing is unavailable.
