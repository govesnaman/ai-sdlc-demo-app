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

## Repository Model and Paths (Baseline Before Step 1)

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

### Stable approver identity resolution

For this in-memory demo, use the authenticated `UserSession.Username` as the stable `UserId`; do not add a separate configured identity field. Normalize it with `Trim().ToLowerInvariant()` before storing it in `ApprovalRecord.UserId` and before duplicate comparisons. This makes identity comparison case-insensitive and insensitive to leading/trailing whitespace. Preserve the role snapshot as the authenticated role string.

### `Invoice`

Update `backend/src/InvoiceApp.Domain/Invoice.cs`:

| Property | Proposed C# type | Change and semantics |
|---|---|---|
| `Approvals` | `List<ApprovalRecord>` | Add, initialized to `new()`. Canonical ordered history of successful approvals; append only after all validation passes. Store one record for each successful single approval and each successful approval in the dual-approval tier. |
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

### Identity propagation

The approval service receives a stable approver identifier and role as primitive inputs; the Domain project does not depend on API authentication types. For this in-memory demo, use `UserSession.Username.Trim().ToLowerInvariant()` as the stable `UserId` for approval history and duplicate comparisons. Do not add a separate configured `UserId` unless the app later adopts an identity provider with a distinct immutable subject identifier.

## Approval Tier Rules

Amounts are `Invoice.Amount` decimal values, compared inclusively against the stated limits.

| Invoice amount | Required approval policy |
|---|---|
| `<= 50,000` | Manager, legacy `Approver`, or Director may approve; one successful approval transitions directly to `Approved`. |
| `> 50,000` and `<= 500,000` | Director may approve; one successful approval transitions directly to `Approved`. A Manager or legacy `Approver` attempt is rejected. |
| `> 500,000` | Two distinct approvers are required. The second successful approval must be by a Director. The first approval transitions to `PartiallyApproved`; the qualifying second approval transitions to `Approved`. |

Amounts in these examples are whole currency units; the implementation must compare the exact `decimal` amount and must not round an amount to a threshold before validation.

### Resolved Role Permission Matrix

The product/policy owner resolved how existing roles map to the approval policy:

| Role | Single-approver permission | First approver for `> 500,000` (Step 2) | Second approver for `> 500,000` |
|---|---|---|---|
| `Manager` | Allowed at `<= 50,000` | Allowed | Not allowed |
| `Approver` (legacy alias) | Same as Manager: allowed at `<= 50,000` | Allowed | Not allowed |
| `Director` | Allowed at `<= 500,000` | Allowed | Allowed and required |
| `Admin` | Not authorized at any amount | Not allowed | Not allowed |
| Any other role | Not authorized at any amount | Not allowed | Not allowed |

`Admin` and unsupported roles must be rejected with `UNAUTHORIZED_ROLE: Role <role> is not authorized to approve invoices`; no approval validation failure may mutate invoice state. The first-approver role for invoices above 500,000 is resolved: Manager, legacy Approver, or Director may be first approver in Step 2. Step 1 does not implement that dual-approver behavior; all single-approver attempts above 500,000 are rejected until Step 2.

## State Transition Matrix

`Approvals` below means the proposed ordered `Invoice.Approvals` collection. “Record?” indicates whether a successful approval is appended. An error leaves all invoice fields untouched. Error codes are stable machine-readable identifiers; before Step 1, the API did not expose a separate error-code field.

| Current Status | Invoice Amount | Approver Role | Prior Approvals | Next Status | Approval Recorded? | Error Code |
|---|---:|---|---|---|---|---|
| `PendingApproval` | `<= 50,000` | `Manager`, legacy `Approver`, or `Director` | None | `Approved` | Yes | None |
| `PendingApproval` | `> 50,000` and `<= 500,000` | `Director` | None | `Approved` | Yes | None |
| `PendingApproval` | `> 50,000` and `<= 500,000` | `Manager` or legacy `Approver` | None | Unchanged | No | `APPROVAL_LIMIT_EXCEEDED` |
| `PendingApproval` | `> 500,000` | Any role in Step 1 | None | Unchanged | No | `APPROVAL_LIMIT_EXCEEDED` (Step 1; dual approval is Step 2) |
| `PendingApproval` | `> 500,000` | `Manager`, legacy `Approver`, or `Director` (Step 2) | None | `PartiallyApproved` | Yes | None |
| `PendingApproval` | Any | `Admin` or any unsupported role | Any | Unchanged | No | `UNAUTHORIZED_ROLE` |
| `PartiallyApproved` | `> 500,000` | Same normalized `UserId` as the first approver | Exactly one record | Unchanged (`PartiallyApproved`) | No | `DUPLICATE_APPROVER` |
| `PartiallyApproved` | `> 500,000` | Any role other than `Director` | Exactly one record by a distinct normalized `UserId` | Unchanged (`PartiallyApproved`) | No | `INVALID_SECOND_APPROVER` |
| `PartiallyApproved` | `> 500,000` | `Director` | Exactly one record by a distinct user | `Approved` | Yes | None |
| `Approved` | Any | Any authorized role | Any | Unchanged | No | `ALREADY_APPROVED` |
| `Draft` or `Rejected` | Any | Any | Any | Unchanged | No | `INVALID_INVOICE_STATUS` |
| Any status | Any | Not authorized to access a role-restricted route | Any | Unchanged | No | HTTP 403; route authorization failure |
| Any status | Any | N/A | Any | Unchanged | No | `INVOICE_NOT_FOUND` (HTTP 404) |

For amount-limit failures where the Manager/Director policy directly specifies the limit, use `APPROVAL_LIMIT_EXCEEDED`. `DUPLICATE_APPROVER` is mandatory when the same `UserId` attempts to satisfy the second-approver slot, even if other validations would also fail; validation precedence should be deterministic and tested. If more than one condition fails, return one stable primary error and do not mutate invoice state.

For a valid `Reject` request, the existing explicit decision behavior is separate: `PendingApproval` (and, per the policy above, `PartiallyApproved`) transitions to `Rejected`, sets `DecidedBy`, `DecidedAt`, and `DecisionNote`, and records no new approval.

## Negative Invariants (Critical)

### Duplicate Approver Invariant

- No two entries in `Invoice.Approvals` for the same invoice may have equal `UserId` values.
- Derive the ID from `UserSession.Username.Trim().ToLowerInvariant()`; compare normalized IDs using case-insensitive equality so capitalization and leading/trailing whitespace cannot bypass this invariant.
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
| Manager/legacy Approver exceeds 50,000 or Director exceeds 500,000 in Step 1 | 409 | `APPROVAL_LIMIT_EXCEEDED` |
| Caller role is `Admin` or otherwise unsupported | 409 | `UNAUTHORIZED_ROLE` |
| Second approval attempted by the first approver's normalized `UserId` | 409 | `DUPLICATE_APPROVER` |
| Second approver is not a Director | 409 | `INVALID_SECOND_APPROVER` |
| Invoice is already `Approved` | 409 | `ALREADY_APPROVED` |
| An approval is attempted from a state that cannot be approved | 409 | `INVALID_INVOICE_STATUS` |
| Invoice does not exist | 404 | `INVOICE_NOT_FOUND` |
| Caller is not signed in or is blocked from a role-restricted route | 401/403 | Existing authentication/authorization response; no invoice mutation |
| Malformed/missing required request data | 400 | Existing validation behavior unless an explicit typed code is added |

Before Step 1, the controller did not pass the session role to `Approve`, and the base controller emitted only `{ error }`. Step 1 passes the role into domain validation and maps `ApprovalRejectedException` to HTTP 409 with `{ "errorCode": "...", "error": "..." }`, without changing success response serialization. The approve route remains behind session authentication; domain validation returns `UNAUTHORIZED_ROLE` for Admin and unsupported roles. The explicit reject route remains role-restricted.

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

## Remaining Decisions

1. Before Step 2, decide whether to add a separate stable `UserId` to configured users and sessions or formally designate the unique username as the stable identifier for duplicate-approver checks.
2. Confirm whether intentional rejection of a `PartiallyApproved` invoice is allowed. This proposal recommends preserving the existing ability to reject an in-progress invoice.

The first-approver permissions above 500,000 and the treatment of `Admin` are resolved by the role matrix above. Step 1 must not implement any dual-approver transitions.
