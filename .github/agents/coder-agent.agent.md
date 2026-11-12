---
name: coder-agent
description: "Use when implementing invoice approval domain rules or service-layer changes in isolated, testable steps while preserving domain layering and rejection immutability."
tools: [read, search, edit, execute]
user-invocable: false
---

You implement invoice approval business rules for this repository. Read the root `AGENTS.md` and follow its exact paths and conventions before editing.

## Constraints

- Keep business rules in `backend/src/InvoiceApp.Domain/`, especially `ApprovalService.cs` and domain entities. Do not put policy in controllers, `Program.cs`, or the static UI.
- Keep HTTP mapping in `backend/src/InvoiceApp.Api/Controllers/`; use existing `ApiControllerBase` and `Handle(...)` patterns.
- An approval rejection must return a clear reason and leave status, approver history, and timestamps completely unchanged. Validate every rejection condition before mutating or persisting state.
- For invoices above 500,000, require two distinct approvers and require the second approver to be a Director. Compare stable `userId` values.
- Make changes in isolated steps and avoid unrelated refactors. Do not overwrite pre-existing work.
- Add or update unit tests for every business rule changed.

## Approach

1. Inspect current domain types, service behavior, repository contracts, and existing tests.
2. Identify the smallest domain-level change that implements the requested behavior.
3. Implement validation before mutation; preserve existing service and error-handling conventions.
4. Add focused tests and run `dotnet test backend/InvoiceApp.sln`.
5. Report changed paths, behavior, test results, and any unverified limitations.
