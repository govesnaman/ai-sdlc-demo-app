# Invoice Approval Limits by Role — Assignment Hand-In

## 1. Overview & Architecture Decisions

This feature enforces approval limits in the Domain service, carries the authenticated role from the API into that service, and preserves invoice state when an approval attempt is rejected.

| Approver role | Approval rule |
|---|---|
| `Manager` | May approve invoices with `amount <= 50,000`. |
| `Approver` | Legacy alias for `Manager`, with the same `<= 50,000` limit. |
| `Director` | May approve invoices with `amount <= 500,000`. |
| Any authorized first approver | May make the first approval for an invoice above `500,000`; the invoice becomes `PartiallyApproved`. |
| Second approver above `500,000` | Must be a distinct user with the `Director` role; the invoice then becomes `Approved`. |
| `Admin` or unsupported role | Approval is rejected with `UNAUTHORIZED_ROLE`. |

`ApprovalsController` passes the authenticated `UserSession.Username` and role to the approval service. The username is normalized using `Trim().ToLowerInvariant()` for the approval record's stable `UserId`; duplicate detection ignores casing and surrounding whitespace. Rejected approval attempts return a descriptive domain error and must not change invoice status, approval history, decision metadata, or timestamps.

Policy and persistence boundaries follow the repository layout: domain rules are in `backend/src/InvoiceApp.Domain/ApprovalService.cs`; request/session and HTTP handling remain in `backend/src/InvoiceApp.Api/Controllers/ApprovalsController.cs` and `backend/src/InvoiceApp.Api/Controllers/ApiControllerBase.cs`; UI behavior is in `backend/src/InvoiceApp.Api/wwwroot/`; API and business rules are tested separately.

## 2. Spec-Driven Development & Phased Commits

The feature requirements and implementation sequence are documented in [specs/approval-limits-spec.md](specs/approval-limits-spec.md) and [specs/task-breakdown.md](specs/task-breakdown.md). Repository conventions are in [AGENTS.md](AGENTS.md); agent roles are defined in [.github/agents/](.github/agents/).

The branch contains five standalone commits: two setup/specification commits, followed by three feature steps.

| Commit | Scope |
|---|---|
| `0cf53e0` | Add `AGENTS.md`, agent instructions, structured logging, and commit hook setup. |
| `25f9e44` | Add the approval-limits specification and phased task breakdown. |
| `821188c` | Step 1: enforce Manager/legacy Approver and Director single-approver limits with boundary tests. |
| `8c59318` | Step 2: add the `> 500,000` dual-approver flow and distinct-user/Director-second-approver invariants. |
| `0757be1` | Step 3: add the Playwright rejection flow and mutation-test hardening. |

The three feature commits implement the sequential steps described in the task breakdown; the first two commits establish the workflow and specification.

## 3. Test Strategy & Verification

- **Unit tests:** xUnit tests in `backend/tests/InvoiceApp.Tests/ApprovalServiceTests.cs` cover the required amount boundaries: `49,999`, `50,000`, `50,001`, `499,999`, `500,000`, and `500,001`, with outcomes appropriate to each role and the dual-approval tier.
- **Negative invariants:** tests cover duplicate approver IDs, casing/whitespace identity variants, non-Director second approvers, unauthorized roles, invalid approval state, and approvals of already-final invoices. Rejections compare serialized invoice state before and after the attempt and assert that the repository was not updated.
- **API contract:** `ApprovalsControllerTests` verifies HTTP 409 responses with `errorCode` and descriptive `error` fields for rejected approvals.
- **Playwright E2E:** the Manager over-limit scenario creates a `75,000` invoice, attempts approval in the UI, checks the visible alert and unchanged pending status/actionability, then verifies persisted status and empty approval history through the API.

Recorded verification for the final Step 3 changes:

```text
dotnet test backend/InvoiceApp.sln
Passed: 38, Failed: 0

cd e2e && npm test
Passed: 1, Failed: 0
```

## 4. Mutation Testing Results (Stryker)

```text
Before (Baseline): 74.74%
After (Hardened):  92.63%
```

The baseline is the pre-hardening Stryker.NET run recorded during this work. The original after score is from Stryker.NET 4.16.0 targeting `ApprovalService.cs`: 92.63% (88 killed, 2 equivalent survivors, 5 no coverage, and 7 compile errors). A repeat run using the pinned 4.13.0 tool scored 92.71% (89 killed, 2 equivalent survivors, 5 no coverage, and 7 compile errors). The two survivors were reviewed as behaviorally equivalent: replacing the `"Director"` tier value with an empty string does not change the selected Director limit, and changing the later `> 500,000` comparison to `>= 500,000` does not alter the `500,000` case because the preceding pending-invoice branch handles amounts through that boundary.

Examples of mutant behavior killed by tests include:

- Flipping the Manager amount limit comparison from `>` to `>=`, which would reject the exact `50,000` boundary.
- Flipping the Director threshold comparison or the high-value pending-state condition.
- Replacing duplicate-history `Any()` with `All()`, which would fail to reject a reused approver.
- Reversing the Director-only second-approver check.
- Mutating the `PartiallyApproved` and approval-history consistency guards.

The root npm `test` script is a placeholder and the root npm Stryker/Vitest runner does not mutate the C# domain. The pinned Stryker.NET 4.13.0 installation and command are documented in `AGENTS.md` and `specs/task-breakdown.md`.

## 5. PR Review Findings & Verdicts

The diff was reviewed against `AGENTS.md`, `.github/agents/reviewer-agent.agent.md`, and repository conventions. No separate workshop review prompt was found in `.github/prompts/` or `docs/`. No actionable approval-domain, API-error-handling, or UI-rejection defect was identified in the reviewed implementation. The findings below concern review reproducibility and completion documentation.

| Finding # | File & Line | Finding & Risk | Actionable Fix | Suggested Verdict |
|---:|---|---|---|---|
| 1 | `AGENTS.md` Stryker command | The previous guidance pointed to an unavailable `dotnet stryker` command, which prevented running mutation tests. | **Fixed:** documented the pinned install, corrected executable `dotnet-stryker`, the standard global-tool PATH location, and the invocation targeting `ApprovalService.cs`; clarified the npm limitation. | Fixed |
| 2 | `specs/task-breakdown.md` mutation results | The previous 92.63% summary omitted mutant categories. | **Fixed:** recorded the exact target and full 4.16.0 breakdown (88 killed, 2 equivalent survivors, 5 no coverage, 7 compile errors), plus a repeat 4.13.0 result. | Fixed |
| 3 | `specs/task-breakdown.md` verification checklists | Step 3 isolation and final acceptance were not marked complete despite the commit sequence and test evidence. | **Fixed:** reconciled checkboxes with commits `0cf53e0`, `25f9e44`, `821188c`, `8c59318`, `0757be1` and verified runs (38 unit tests, 1 E2E test). | Fixed |

All three findings are fixed in the documentation-polish pass; the Stryker executable name and PATH prerequisite are clarified here without changing application code.

## Demo Video

[Watch 8–10 Minute Demo Video](<PASTE_VIDEO_URL_HERE>)
