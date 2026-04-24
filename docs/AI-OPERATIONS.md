# AI operations assist (expenses + timesheets)

This document is the “why we built it this way” narrative for demos and grading: **meaningful AI** (OpenAI Chat Completions API) plus **deterministic rules** so the product still works when the LLM is off, slow, or wrong.

## Product goal

- **Expenses:** reduce approval ping-pong by surfacing **receipt gaps**, **thin business purpose**, and **category mismatches** *before* submit.
- **Timesheets:** catch **week-hour extremes**, **per-day overload**, **duplicate merge keys**, and **under-documented heavy blocks** *before* save/submit.

Neither flow **auto-writes** to the database. The user stays in control; AI is advisory.

## Architecture decisions (talk track)

### 1) Server-side LLM only

**Decision:** All OpenAI calls run in the ASP.NET Core API (`OperationsAiAdvisor`), never from the browser.

**Why:** API keys stay off the client; you avoid CORS/auth leakage; you can add logging, timeouts, and future rate limits in one place.

### 2) Same config as existing staffing AI

**Decision:** Reuse `AIRecommendations` (`Provider`, `OpenAiApiKey`, `OpenAiModel`, `OpenAiBaseUrl`, `OpenAiTimeoutSeconds`, `OpenAiTemperature`) from `appsettings.*` / env.

**Why:** One operational knob for the course (“turn on OpenAI”) instead of a second secret store. `Provider` must be `openai` or `hybrid` **and** `OpenAiApiKey` must be set for the LLM layer to run; otherwise you still get **heuristic-only** reviews (still “real” value for demos without spend).

### 3) Hybrid = rules first, LLM second

**Decision:** C# heuristics always run. The model receives the draft JSON **and** the list of heuristic `code`s so it should **not** parrot the same issues.

**Why:** Deterministic checks are auditable and cheap. The LLM adds **nuance** (wording of questions, optional note templates) grounded in the same JSON payload. If OpenAI fails, users still get the rules.

### 4) Strict JSON from the model

**Decision:** Chat Completions with `response_format: { "type": "json_object" }` and a tight schema in the prompt.

**Why:** Easier to parse than free text; reduces rambling; aligns with how `OpenAiStaffingReranker` already integrates.

### 5) Human-in-the-loop UX

**Decision:** Separate buttons **“Review draft (AI + Rules)”** on Expenses and **“Review week (AI + Rules)”** on Time Tracking; results render in `AiReviewPanel` (read-only).

**Why:** No surprise mutations; clear story for compliance: “suggestions, not decisions.”

### 6) RBAC and data scope

**Decision:** Endpoints require a normal JWT (`[Authorize]`). The request body only contains what the user already typed on the form—no cross-user reads for this feature.

**Why:** Keeps the feature simple and avoids leaking manager/team aggregates into the LLM prompt in v1.

## HTTP surface

| Method | Path | Body |
|--------|------|------|
| POST | `/api/ai/operations/expense-review` | `OperationsExpenseAiReviewRequest` |
| POST | `/api/ai/operations/timesheet-week-review` | `OperationsTimesheetWeekAiReviewRequest` |

## Enable OpenAI locally

In `api/appsettings.Development.json` (or user-secrets / env):

```json
"AIRecommendations": {
  "Provider": "openai",
  "OpenAiApiKey": "sk-..."
}
```

Leave `Provider` as `deterministic` for zero-cost local runs; heuristics still return useful flags.

## Known limitations (honest demo points)

- The model only sees **structured fields** you send—not PDF invoice bytes (vision would be a follow-up).
- No persistence of prompts/responses yet (add if your course requires audit logs).
- No org-specific policy document ingestion (RAG) in v1—prompts are generic “professional services” guardrails.
