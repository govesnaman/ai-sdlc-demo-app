# Invoice Approval Limits: Sequential Task Breakdown

## Status and Scope

- **Document type:** implementation plan (OpenSpec / Spec Kit-style tasks and acceptance checklist)
- **Status:** proposed; no application or test code is changed by this document
- **Specification:** [approval-limits-spec.md](./approval-limits-spec.md)
- **Required project guidance:** `AGENTS.md`
- **Commit plan:** three strictly sequential commits; do not start a later step until its predecessor's checklist passes and its commit is recorded.

## Pre-Implementation Gate

The role policy is resolved and recorded in `approval-limits-spec.md` and `AGENTS.md`:

- `Approver` is a legacy alias for Manager.
- Manager and Approver may be first but not second approvers above 500,000 in Step 2.
- Director may be first or second and is required as the second approver above 500,000 in Step 2.
- Admin and all unsupported roles cannot approve.

The stable identity decision is resolved: `UserSession.Username.Trim().ToLowerInvariant()` is the approval `UserId`, with case-insensitive duplicate checks. Intentional rejection of a partially approved invoice remains supported. Step 1 rejects all single approval attempts above 500,000; Step 2 implements the two-approver flow.

## Step 1 (Commit 1): Single-Approver Limits

### Scope

Implement Manager and legacy `Approver` approval for invoices `<= 50,000` and Director approval for invoices `<= 500,000`. Reject `Admin` and unsupported roles with `UNAUTHORIZED_ROLE`. An amount above the acting role's single-approver ceiling must be refused with a descriptive error. Do not implement `PartiallyApproved` or count multiple approvals in this commit.

### Paths

- Domain policy and error type: `backend/src/InvoiceApp.Domain/ApprovalService.cs` and `backend/src/InvoiceApp.Domain/ApprovalRejectedException.cs`.
- API role propagation and error mapping: `backend/src/InvoiceApp.Api/Controllers/ApprovalsController.cs`, `backend/src/InvoiceApp.Api/Controllers/ApiControllerBase.cs`, and role/user configuration in `backend/src/InvoiceApp.Api/appsettings.json`.
- Unit and API contract tests: `backend/tests/InvoiceApp.Tests/ApprovalServiceTests.cs`, `backend/tests/InvoiceApp.Tests/ApprovalsControllerTests.cs`, and `backend/tests/InvoiceApp.Tests/FakeInvoiceRepository.cs`.

### Required tests

Test exact amounts **49,999**, **50,000**, **50,001**, **499,999**, **500,000**, and **500,001** against Manager and Director decisions as applicable. Include successful inclusive-boundary assertions and failures immediately above each applicable limit. Verify invoices above 500,000 are not approved by a single approver in this step.

For every failed approval, assert the descriptive failure and exact before/after equality for all Invoice fields and approval history. Assert that the repository is not updated on the failed attempt. Include pre-populated decision metadata and timestamps in at least one failure case so accidental overwrites are detected.

### Verification checklist

- [ ] Manager succeeds at 49,999 and 50,000.
- [ ] Manager is rejected at 50,001 without any invoice mutation.
- [ ] Legacy `Approver` succeeds through 50,000 and is rejected above 50,000 as the Manager alias.
- [ ] Director succeeds at 49,999, 50,000, 50,001, 499,999, and 500,000.
- [ ] Director cannot single-approve 500,001.
- [ ] `Admin` and unsupported roles receive `UNAUTHORIZED_ROLE` without invoice mutation.
- [ ] Limit and role errors are clear and map to HTTP 409 with `errorCode` and `error` fields.
- [ ] Failed attempts leave `Status`, `Approvals` (if introduced), `CreatedAt`, `DecidedBy`, `DecidedAt`, and `DecisionNote` unchanged and do not persist.
- [ ] `dotnet test backend/InvoiceApp.sln` passes.
- [ ] Commit 1 contains only Step 1 production changes and their direct unit tests/documentation needed for those changes.

## Step 2 (Commit 2): Two-Approver Workflow

### Scope

Implement the `> 500,000` path using `InvoiceStatus.PartiallyApproved`, an ordered approval history, two distinct stable `UserId` values, and the required Director role for the second approval. No invoice above 500,000 can reach `Approved` after only one successful approval. The role and identity decisions are resolved in the role matrix and stable-identity section above.

### Paths

- Domain entity and approval history: add `backend/src/InvoiceApp.Domain/ApprovalRecord.cs`; update `backend/src/InvoiceApp.Domain/Invoice.cs` and `backend/src/InvoiceApp.Domain/InvoiceStatus.cs`.
- Domain transitions and atomic validation: `backend/src/InvoiceApp.Domain/ApprovalService.cs`.
- Identity, roles, routes, and error mapping: `backend/src/InvoiceApp.Api/Auth/UserSession.cs`, `backend/src/InvoiceApp.Api/Auth/RequireRoleAttribute.cs`, `backend/src/InvoiceApp.Api/Controllers/ApprovalsController.cs`, `backend/src/InvoiceApp.Api/Controllers/ApiControllerBase.cs`, and configured users in `backend/src/InvoiceApp.Api/appsettings.json`.
- Tests: `backend/tests/InvoiceApp.Tests/ApprovalServiceTests.cs` and `backend/tests/InvoiceApp.Tests/FakeInvoiceRepository.cs`.

### Required negative and transition tests

- First valid approval produces `PartiallyApproved` with exactly one `ApprovalRecord` and leaves final-decision metadata unchanged.
- A distinct Director second approval produces `Approved`, appends exactly one record, and sets final decision metadata according to the specification.
- Every approval record stores normalized `UserId = UserSession.Username.Trim().ToLowerInvariant()`, the role, and its UTC approval timestamp.
- Reusing the first approver's normalized `UserId` returns `DUPLICATE_APPROVER`, including capitalization and outer-whitespace variants; no mutation or repository update occurs.
- A non-Director second approver returns `INVALID_SECOND_APPROVER`; no mutation or repository update occurs.
- Any attempt to approve an already approved invoice returns `ALREADY_APPROVED` without mutation or repository update.
- The first approval cannot skip directly to `Approved`.
- A second approval cannot be recorded when the invoice is already final or when the approval history is inconsistent.
- All rejection cases compare the full before/after state, including collection contents and ordering, all timestamps, `DecidedBy`, `DecidedAt`, and `DecisionNote`.
- Explicit invoice rejection continues to require a reason and does not erase prior approval history.

### Verification checklist

- [x] The role and identity policy decisions are documented and implementation behavior matches those decisions.
- [x] `PartiallyApproved` is used only while exactly one qualifying approval is recorded for the dual-approval tier.
- [x] No normalized `UserId` is counted twice; identity comparison ignores case and surrounding whitespace.
- [x] The second approver above 500,000 is a Director.
- [x] Each failed approval returns the specified error and performs zero invoice/repository mutations.
- [x] API status, `errorCode`, and `error` values match the contract.
- [x] `dotnet test backend/InvoiceApp.sln` passes, including threshold and negative-invariant tests.
- [ ] Commit 2 depends on Commit 1 and includes only the dual-approval domain/API behavior and its direct unit tests.

## Step 3 (Commit 3): UI E2E and Mutation Verification

### Scope

Add a Playwright end-to-end scenario that exercises a rejected over-limit approval through the actual UI and confirms the invoice remains in its original status and approval state as visible to the user. Run mutation testing against the approval policy and add/fix tests to kill relevant mutants.

### Paths and commands

- UI behavior: `backend/src/InvoiceApp.Api/wwwroot/app.js`, `backend/src/InvoiceApp.Api/wwwroot/index.html`, and `backend/src/InvoiceApp.Api/wwwroot/styles.css` only if a UI change is needed.
- Playwright tests and page objects: `e2e/tests/` and `e2e/pages/`; runner: `e2e/playwright.config.ts`.
- E2E command from the repository root: `cd e2e && npm test`.
- Unit baseline: `dotnet test backend/InvoiceApp.sln`.
- C# mutation testing uses the Stryker.NET global tool; the root npm Stryker/Vitest runner does not mutate C# code. Install the pinned tool and run from the unit-test project directory:
  - `dotnet tool install --global dotnet-stryker --version 4.13.0`
  - The executable is `dotnet-stryker`, not `dotnet stryker`. If it is not found, add `$HOME/.dotnet/tools` (macOS/Linux) or `%USERPROFILE%\.dotnet\tools` (Windows) to `PATH`, then open a new shell.
  - `cd backend/tests/InvoiceApp.Tests && dotnet-stryker --project InvoiceApp.Domain.csproj --test-project InvoiceApp.Tests.csproj --mutate '**/ApprovalService.cs' --concurrency 2 --reporter ClearTextTree --reporter Json --output /tmp/stryker-approval-service`
- The recorded Step 3 run with Stryker.NET 4.16.0 targeted `ApprovalService.cs` and scored 92.63% (88 killed, 2 equivalent survivors, 5 no coverage, and 7 compile errors). A repeat run using the pinned 4.13.0 tool scored 92.71% (89 killed, 2 equivalent survivors, 5 no coverage, and 7 compile errors). The equivalent survivors were reviewed: replacing the `"Director"` tier value with an empty string still selects the Director limit because only `"Manager"` selects the lower limit; changing `>` to `>=` in the later high-value branch cannot affect the exact 500,000 boundary because the preceding pending-invoice branch handles amounts through 500,000.

### E2E and mutation targets

- Exercise a Manager (or other role chosen by the policy decision) attempting an approval beyond that role's approved limit, using the existing authenticated app flow.
- Assert the visible alert presents a clear rejection message.
- Assert the invoice remains at its prior status in the invoice table and remains actionable in the pending approval UI when applicable.
- Verify no success-state transition or optimistic client-side status change occurs.
- Target mutation cases for threshold comparator direction/inclusivity, removal of role validation, duplicate-`UserId` checking, second-approver Director checking, and state mutation/repository update before validation.
- Preserve required user-facing and keyboard/accessibility behavior; use Playwright role/label locators, not CSS or XPath selectors.

### Verification checklist

- [x] E2E scenario uses the configured local server and existing Playwright setup, with no external service dependency.
- [x] E2E observes the rejection alert and unchanged invoice status/actionability.
- [x] `cd e2e && npm test` passes.
- [x] `dotnet test backend/InvoiceApp.sln` passes.
- [x] A compatible .NET mutation runner is used for C# mutation verification; root npm Stryker is not treated as evidence.
- [x] Mutation report shows targeted approval-policy mutants killed; both surviving mutants are documented as behaviorally equivalent.
- [x] Commit `0757be1` follows Commits `821188c` and `8c59318` and contains the Step 3 E2E/UI verification and mutation-test hardening; the preceding setup/spec commits are `0cf53e0` and `25f9e44`.

## Final Acceptance

- [x] Pre-implementation role and identity decisions are documented in the specification and `AGENTS.md`.
- [x] The three feature commits (`821188c`, `8c59318`, `0757be1`) follow the sequential implementation plan after setup/spec commits `0cf53e0` and `25f9e44`.
- [x] The final implementation preserves the domain/API/UI layering in `AGENTS.md`.
- [x] Rejected approval attempts return useful errors and leave invoice state unchanged.
- [x] `dotnet test backend/InvoiceApp.sln` passed (38 tests), `cd e2e && npm test` passed (1 test), and Stryker.NET results for `ApprovalService.cs` are recorded with their complete mutant-status breakdowns.
