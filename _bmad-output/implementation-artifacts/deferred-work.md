# Deferred work

## Deferred from: code review of S-E1-01-email-password-sign-in.md (2026-04-05)

- **Login rate limiting:** `POST /api/auth/login` has no throttle or lockout; story implementation notes mention MVP-level rate limiting as acceptable follow-up.
- **In-memory EF:** API uses `UseInMemoryDatabase` for bootstrap; replace with real SQL Server / migrations per architecture before production.
- **Idle session vs absolute JWT expiry:** Tokens expire on a fixed clock from issuance; architecture’s 30-minute idle behavior waits on refresh/session hardening.

## Deferred from: code review of S-E1-02-admin-user-account-lifecycle.md (2026-04-05)

- **`/api/auth/me` 401 body:** Returns empty 401 without `AuthErrorResponse` when subject invalid, user missing, or inactive; SPA treats as generic session failure — acceptable follow-up for consistent JSON error envelope.

## Deferred from: code review of S-E1-03-assign-change-user-roles.md (2026-04-05)

- **Last-admin protection vs concurrency:** `UsersController.Patch` checks “other active admin” with `AnyAsync` then `SaveChanges`; two concurrent demotes of the last two admins could both pass the check. MVP-typical; mitigate later with transaction isolation, serialized admin mutations, or domain constraints if the threat model requires it.

## Deferred from: code review of S-E1-04-enforce-rbac-restricted-operations.md (2026-04-06)

- **`RbacRoleSets.AdminAndFinance` bundles three matrix rows:** If org-timesheets, billing-rates, and invoice-generate ever need different role sets, split into separate constants or named policies.
- **`web/dist/` in version control:** Generated SPA assets add review noise; prefer CI build or ignore `dist` unless deployment model requires committed artifacts.

## Deferred from: code review of S-E9-01-personal-utilization-reports.md (2026-04-21)

- **`personal-summary` missing-query guard:** `ReportsController.PersonalSummary` passes nullable query strings into `TryParseDateOnly` (which trims input), so missing `from`/`to` can throw before the intended 400 response. Pre-existing in summary path; defer to a shared date-validation hardening pass.

## Deferred from: code review of S-E9-02-manager-team-reports.md (2026-04-21)

- **`personal-summary` null query parsing risk:** `ReportsController.PersonalSummary` still parses nullable `from`/`to` without null-coalescing, so missing params may throw before the expected 400 response. Pre-existing behavior outside S-E9-02 scope; defer to shared reports date-validation hardening.

## Deferred from: code review of S-E9-04-report-filters.md (2026-04-21)

- **Reports page request race:** `Reports.tsx` `load()` can allow an older in-flight response to overwrite newer period state when users change period quickly. Existing reports flow behavior; defer to a follow-up that adds request-id or cancellation guards.
