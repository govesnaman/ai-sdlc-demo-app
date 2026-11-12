# Invoice Approval Limits by Role

## Status and Scope

- **Document type:** feature specification (OpenSpec / Spec Kit-style change proposal)
- **Status:** proposed; no application behavior is changed by this document
- **Owners by layer:** Domain rules in `backend/src/InvoiceApp.Domain/`; HTTP contract in `backend/src/InvoiceApp.Api/Controllers/`; UI in `backend/src/InvoiceApp.Api/wwwroot/`; unit tests in `backend/tests/InvoiceApp.Tests/`; browser tests in `e2e/tests/`
- **Related guidance:** `AGENTS.md`, especially “Feature Context: Invoice Approval Limits by Role”

This specification defines role-based approval limits, approval history, and dual-approver governance. It separates the repository's verified current shape from the proposed changes needed to support the feature.

## Overview and Problem Statement

The current service allows any caller reaching the approval endpoint to approve a pending invoice as a single decision. It does not enforce amount thresholds or approver roles in the domain, has no approval-history collection, and does not support a partially approved state. The API endpoint currently passes the session username into the service, while authorization currently recognizes `Approver` and `Admin`, not `Manager` or `Director`.

The feature must enforce:

1. A Manager may approve an invoice up to and including 50,000.
2. A Director may approve an invoice up to and including 500,000.
3. An invoice above 500,000 requires two distinct approvers, with the second approver a Director.
4. An invalid approval attempt returns a clear reason and makes no invoice-state changes.

An explicit invoice rejection through the existing `Reject` endpoint is a valid business decision that transitions a pending invoice to `Rejected`. The atomic rejection invariant in this feature is about an **approval attempt refused by validation**, not about the separate intentional `Reject` action.

## Current Repository Model and Paths

### Domain

`backend/src/InvoiceApp.Domain/Invoice.cs` currently defines:

| Property | Current C# type | Current default/meaning |
|---|---|---|
| `Id` | `string` | Empty string; invoice identifier |
| `CustomerName` | `string` | Empty string |
| `Amount` | `decimal` | Invoice amount |
| `Status` | `InvoiceStatus` | `InvoiceStatus.Draft` |
| `CreatedAt` | `DateTime` | `DateTime.UtcNow` |
| `DecidedBy` | `string?` | Null until decision |
| `DecidedAt` | `DateTime?` | Null until decision |
| `DecisionNote` | `string?` | Optional decision/rejection note |

`backend/src/InvoiceApp.Domain/InvoiceStatus.cs` currently contains `Draft`, `PendingApproval`, `Approved`, and `Rejected`. `PartiallyApproved` does not exist yet.

`backend/src/InvoiceApp.Domain/ApprovalService.cs` exposes `Submit(string invoiceId)`, `Approve(string invoiceId, string approver)`, and `Reject(string invoiceId, string approver, string reason)`. `Approve` currently checks only `PendingApproval`, then sets `Status`, `DecidedBy`, and `DecidedAt` before calling `IInvoiceRepository.Update`.

### Identity and roles

- `backend/src/InvoiceApp.Api/Auth/UserSession.cs` currently defines `UserSession(string Username, string Role)` and a `DemoUser` with string `Username`, `Password`, and `Role` properties. There is no `UserId` field.
- `backend/src/InvoiceApp.Api/Controllers/ApprovalsController.cs` has `[RequireRole("Approver", "Admin")]`; its approve action passes only `HttpContext.GetSession().Username` to `ApprovalService`.
- `backend/src/InvoiceApp.Api/Auth/RequireRoleAttribute.cs` authorizes by matching role strings.
- `backend/src/InvoiceApp.Api/appsettings.json` currently configures demo roles `Approver` and `Admin`; it has no Manager or Director entries.
- `backend/src/InvoiceApp.Api/Controllers/AuthController.cs` creates `UserSession` and returns a login response from the configured username and role.

The feature therefore needs role information and a stable user identifier to reach the domain decision. Do not equate username and `userId` without an explicit identity decision.

### API, UI, and tests

- Approval routes are `POST /api/approvals/{invoiceId}/approve` and `POST /api/approvals/{invoiceId}/reject` in `backend/src/InvoiceApp.Api/Controllers/ApprovalsController.cs`.
- `backend/src/InvoiceApp.Api/Controllers/ApiControllerBase.cs` currently maps `KeyNotFoundException` to 404 `{ "error": "..." }`, `InvalidOperationException` to 409 `{ "error": "..." }`, and `ArgumentException` to 400 `{ "error": "..." }`.
- `backend/src/InvoiceApp.Api/wwwroot/app.js` displays API `error` text in `#decision-error` (`role="alert"` in `backend/src/InvoiceApp.Api/wwwroot/index.html`). It refreshes invoice data after a successful decision, not after a failed one.
- Current unit tests are in `backend/tests/InvoiceApp.Tests/ApprovalServiceTests.cs`; they cover basic lifecycle and current single-approver behavior but not roles, thresholds, approval history, or atomic state equality.
- `e2e/tests/` currently contains no Playwright test implementation (only `.gitkeep`); test runner configuration is `e2e/playwright.config.ts`.

## Domain Model and Schema Changes

These are proposed model changes, not existing types. Keep domain types in `backend/src/InvoiceApp.Domain/`.

### `ApprovalRecord`

Add `backend/src/InvoiceApp.Domain/ApprovalRecord.cs`, following the existing mutable domain-entity style:

| Property | Proposed C# type | Requirement |
|---|---|---|
| `UserId` | `string` | Stable identity of the approver; must not be a display name or per-session token |
| `Role` | `string` | Role snapshot at approval time; values used for this policy are `Manager` and `Director` |
| `ApprovedAt` | `DateTime` | UTC timestamp of the recorded approval |

Initialize non-null strings to `string.Empty`, consistent with `Invoice`. One record represents one successful approval action, not an attempted or rejected action.

### `Invoice`

Update `backend/src/InvoiceApp.Domain/Invoice.cs`:

| Property | Proposed C# type | Change and semantics |
|---|---|---|
| `Approvals` | `List<ApprovalRecord>` | Add, initialized to `new()`. Canonical ordered history of successful approvals; append only after all validation passes. |
| `Status` | `InvoiceStatus` | Continue using the enum; add the `PartiallyApproved` member as described below. |
| `DecidedBy` | `string?` | Preserve for compatibility; set to the final approver on transition to `Approved`, and to the rejecting user on intentional transition to `Rejected`. Do not change it on a failed attempt or partial approval. |
| `DecidedAt` | `DateTime?` | Preserve for compatibility; set only on final approval or intentional rejection. Do not change it on a failed attempt or partial approval. |
| `DecisionNote` | `string?` | Preserve existing rejection-note behavior; set only on intentional rejection. |

All other current fields retain their names, types, and meanings. A partial approval appends one `ApprovalRecord` and sets `Status = PartiallyApproved`; the existing final-decision fields remain untouched until the invoice is finally approved or intentionally rejected.

### `InvoiceStatus`

Add `PartiallyApproved` to `backend/src/InvoiceApp.Domain/InvoiceStatus.cs`. The intended lifecycle becomes:

`Draft -> PendingApproval -> PartiallyApproved -> Approved`

For invoices at or below 500,000 that satisfy the applicable role limit, the transition remains:

`PendingApproval -> Approved`

Intentional rejection remains:

`PendingApproval -> Rejected`

The treatment of intentionally rejecting an already `PartiallyApproved` invoice should remain supported unless product policy explicitly prohibits it; preserve the rejection reason and final decision metadata without removing the existing approval history.

### Identity propagation prerequisite

The approval service must receive the approver's stable `UserId` and role, rather than only the current username string. This requires changes to the authentication/session boundary (`backend/src/InvoiceApp.Api/Auth/UserSession.cs`, `backend/src/InvoiceApp.Api/Auth/SessionStore.cs`, `backend/src/InvoiceApp.Api/Controllers/AuthController.cs`, and configured `DemoUser` data in `backend/src/InvoiceApp.Api/appsettings.json`) and to `ApprovalsController`. The domain must not depend on API authentication types; pass primitive domain inputs or a domain-level approver value.

The current app has no account identifier distinct from `Username`. Decide whether configured usernames become stable IDs or whether a separate configured `UserId` is added before implementation. This choice must not compromise the duplicate-approver invariant.

## Approval Tier Rules

Amounts are `Invoice.Amount` decimal values, compared inclusively against the stated limits.

| Invoice amount | Required approval policy |
|---|---|
| `<= 50,000` | Manager or Director may approve; one successful approval transitions directly to `Approved`. |
| `> 50,000` and `<= 500,000` | Director may approve; one successful approval transitions directly to `Approved`. A Manager approval is rejected. |
| `> 500,000` | Two distinct approvers are required. The second successful approval must be by a Director. The first approval transitions to `PartiallyApproved`; the qualifying second approval transitions to `Approved`. |

Amounts in these examples are whole currency units; the implementation must compare the exact `decimal` amount and must not round an amount to a threshold before validation.

### Policy decisions required before Step 2 implementation

The stated rules do not identify which role may make the **first** approval above 500,000: Manager's stated ceiling is 50,000 and Director's is 500,000, while the second approver is explicitly required to be a Director. They also do not state whether the existing `Admin` role bypasses limits or maps to an approval tier. The requirements and current code do not resolve these questions. Product/policy owner must decide both before implementing the first `PartiallyApproved` transition. Do not silently treat an over-limit first approver as authorized or map Admin to Director.

Until that policy is decided, the rows for the first approval above 500,000 in the matrix are conditional and constitute an implementation blocker. The second-approver constraints are fully specified.

## State Transition Matrix

`Approvals` below means the proposed ordered `Invoice.Approvals` collection. “Record?” indicates whether a successful approval is appended. An error leaves all invoice fields untouched. Error codes in this proposal are stable machine-readable identifiers; the current API does not yet expose an error-code field.

| Current Status | Invoice Amount | Approver Role | Prior Approvals | Next Status | Approval Recorded? | Error Code |
|---|---:|---|---|---|---|---|
| `PendingApproval` | `<= 50,000` | `Manager` or `Director` | None | `Approved` | Yes | None |
| `PendingApproval` | `> 50,000` and `<= 500,000` | `Director` | None | `Approved` | Yes | None |
| `PendingApproval` | `<= 50,000` | `Manager` or `Director` | Any existing record | Unchanged | No | `APPROVAL_ALREADY_RECORDED` |
| `PendingApproval` | `> 50,000` and `<= 500,000` | `Manager` | None | Unchanged | No | `APPROVAL_LIMIT_EXCEEDED` |
| `PendingApproval` | `> 500,000` | First-approver role (not yet specified) | None | `PartiallyApproved` if that role is authorized; otherwise unchanged | Yes only if authorized | `FIRST_APPROVER_ROLE_UNDEFINED` until policy is decided |
| `PartiallyApproved` | `> 500,000` | Same `UserId` as the first approver | Exactly one record | Unchanged (`PartiallyApproved`) | No | `DUPLICATE_APPROVER` |
| `PartiallyApproved` | `> 500,000` | Any role other than `Director` | Exactly one record by a distinct user | Unchanged (`PartiallyApproved`) | No | `DIRECTOR_REQUIRED` |
| `PartiallyApproved` | `> 500,000` | `Director` | Exactly one record by a distinct user | `Approved` | Yes | None |
| `Draft`, `Approved`, or `Rejected` | Any | Any | Any | Unchanged | No | `INVALID_INVOICE_STATUS` |
| Any approval-eligible status | Any | Not authorized to access the approval route | Any | Unchanged | No | HTTP 403; route authorization failure |
| Any status | Any | N/A | Any | Unchanged | No | `INVOICE_NOT_FOUND` (HTTP 404) |

For amount-limit failures where the Manager/Director policy directly specifies the limit, use `APPROVAL_LIMIT_EXCEEDED`. `DUPLICATE_APPROVER` is mandatory when the same `UserId` attempts to satisfy the second-approver slot, even if other validations would also fail; validation precedence should be deterministic and tested. If more than one condition fails, return one stable primary error and do not mutate invoice state.

For a valid `Reject` request, the existing explicit decision behavior is separate: `PendingApproval` (and, per the policy above, `PartiallyApproved`) transitions to `Rejected`, sets `DecidedBy`, `DecidedAt`, and `DecisionNote`, and records no new approval.

## Negative Invariants (Critical)

### Duplicate Approver Invariant

- No two entries in `Invoice.Approvals` for the same invoice may have equal `UserId` values.
- A retry by the first approver cannot satisfy the second-approval requirement, regardless of role changes or display-name differences.
- Reject the attempt with code `DUPLICATE_APPROVER` and a human-readable explanation.
- Do not append a record, change status, update final-decision fields, or alter any timestamp.

### Atomic Rejection Invariant

For **every approval validation failure**, including invalid status, role/amount mismatch, missing required Director, or duplicate approver:

- Return a clear human-readable reason and a stable error code.
- Mutate zero fields on the `Invoice`: `Id`, `CustomerName`, `Amount`, `Status`, `CreatedAt`, `DecidedBy`, `DecidedAt`, `DecisionNote`, and the contents/order of `Approvals` must be identical before and after the call.
- Do not call `IInvoiceRepository.Update` for the rejected attempt.
- Validate all applicable conditions before changing an invoice or appending approval history.

The intentional `Reject` action is not an approval-validation failure and intentionally changes decision state. It must continue to require a non-empty reason as it does today.

## API and UI Error Contract

### Proposed HTTP response

For a business-rule approval refusal, return HTTP **409 Conflict**, consistent with the current `ApiControllerBase` mapping of `InvalidOperationException`. Extend the current response shape, which is `{ "error": "..." }`, to include a stable code while retaining the existing `error` property consumed by the UI:

```json
{
  "errorCode": "DUPLICATE_APPROVER",
  "error": "This user has already approved this invoice and cannot approve it a second time."
}
```

Use the corresponding stable code and descriptive message for each rule failure. The minimum mapping is:

| Condition | HTTP status | `errorCode` |
|---|---:|---|
| Manager exceeds 50,000 or Director exceeds the applicable single-approver limit | 409 | `APPROVAL_LIMIT_EXCEEDED` |
| Second approval attempted by the first approver's `UserId` | 409 | `DUPLICATE_APPROVER` |
| Second approver is not a Director | 409 | `DIRECTOR_REQUIRED` |
| An approval is attempted from a state that cannot be approved | 409 | `INVALID_INVOICE_STATUS` |
| Invoice does not exist | 404 | `INVOICE_NOT_FOUND` |
| Caller is not authorized for the route | 403 | Existing route authorization response; no invoice mutation |
| Malformed/missing required request data | 400 | Existing validation behavior unless an explicit typed code is added |

The current controller has no role parameter in its `Approve` service call, and the current base controller emits only `{ error }`. Implementing this contract requires carrying identity and role into domain validation and extending the API error mapping without changing success response serialization unnecessarily.

### UI behavior

On approval failure, `backend/src/InvoiceApp.Api/wwwroot/app.js` must display the response's human-readable `error` text in `#decision-error` (`role="alert"`). If the code is available, the UI may use `errorCode` for diagnostics, but it must not replace the human-readable reason with a raw code.

Do not optimistically change the status, remove the invoice from the pending list, append an approval, or clear/refresh into a success-shaped state after failure. The API is authoritative and unchanged-state invariant is server-side; the UI must leave the visible invoice status unchanged and keep the pending item available after a rejected attempt. Existing behavior already refreshes only on a successful decision and displays caught errors; E2E coverage must verify this behavior and its accessibility alert.

## Acceptance Criteria

1. Each amount boundary behaves according to the threshold table, without rounding errors.
2. A Manager can approve 50,000 and cannot single-approve 50,001.
3. A Director can approve 500,000 and cannot single-approve 500,001.
4. Invoices above 500,000 cannot become `Approved` from only one approval.
5. A second approval can complete an over-500,000 invoice only when it is from a Director with a distinct `UserId`.
6. Every rejected approval reports a stable error code and descriptive text and changes no invoice property, approval history item, timestamp, or repository state.
7. Identity and role are provided by authenticated session data; policy remains in Domain.
8. The UI presents the failure reason and leaves the invoice visibly unchanged and actionable.
9. Unit, API-contract, and Playwright coverage exercises required threshold and negative-invariant cases.

## Open Questions / Blocking Decisions

1. Which role may make the first approval of an invoice above 500,000, given both stated single-approver ceilings?
2. Does existing `Admin` retain an override, map to Director, or become non-approving for this feature? This spec does not assume any of these.
3. Should `UserId` be added to configured users and sessions, or should the current unique username be formally designated as the stable identifier?
4. Does product policy allow explicit rejection of a `PartiallyApproved` invoice? This proposal recommends preserving the current ability to reject after any in-progress approval.

Resolve questions 1–3 before implementation can claim complete enforcement of all roles and dual-approval entry behavior.
