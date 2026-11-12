# Project conventions for AI agents

Invoice approval demo app: ASP.NET Core 8 Web API with a small static web UI.

## Layout

- `backend/src/InvoiceApp.Domain`: business rules, no web or storage code. `ApprovalService` owns the invoice lifecycle (Draft, PendingApproval, Approved, Rejected).
- `backend/src/InvoiceApp.Api`: controllers, sign-in (`Auth/`), in-memory repository, and the web UI under `wwwroot/`.
- `backend/tests/InvoiceApp.Tests`: xUnit tests, one test class per class under test, named `<ClassUnderTest>Tests`.
- `e2e`: Playwright end-to-end tests (`tests/`) and page objects (`pages/`).
- `docs/use-cases`: short briefs for features we may build.

## Conventions

- Controllers live in `Controllers/`, one per resource, route `api/[controller]`. Request bodies are `record` types declared inside the controller.
- Controllers inherit `ApiControllerBase` and use `Handle(...)` so service errors map to 404, 409 and 400.
- Every endpoint is behind sign-in. Restrict destructive actions with `[RequireRole("...")]` (Admin only). Approval actions use the role policy below: the approve service must reject unsupported roles with a structured error without mutating invoice state; the explicit reject action uses `[RequireRole("Manager", "Approver", "Director", "Admin")]`.
- Business rules go in the Domain project, never in controllers.
- Register services in `Program.cs`. Repositories are interfaces in Domain with an in-memory implementation in Api.
- Every new rule or endpoint needs tests. Use `FakeInvoiceRepository` in tests.
- Use clear names; no one-letter variables or `flag` parameters.
- End-to-end tests use page objects and role-based locators (`getByRole`, `getByLabel`), never CSS or XPath selectors.

## Commands

- Run the app: `dotnet run --project backend/src/InvoiceApp.Api` then open http://localhost:5000
- Run the tests: `dotnet test backend/InvoiceApp.sln`
- Run end-to-end tests: `cd e2e && npm install && npx playwright install chromium && npm test`

## Demo accounts (not real credentials)

- `demo.approver` / `ChangeMe123!` (role Approver)
- `demo.director` / `ChangeMe123!` (role Director)
- `demo.admin` / `ChangeMe123!` (role Admin)

## Never

- Commit real credentials, tokens or customer data.
- Add business logic to `Program.cs` or controllers.

## Feature Context: Invoice Approval Limits by Role

This section defines the intended approval policy and implementation guidance for invoice approval limits. Preserve the existing layering: enforce approval rules in the Domain project, and keep controllers focused on HTTP concerns.

### Approval Tier Thresholds

| Role                      | Single-approver permission              | First approver for `> 500,000` (Step 2) | Second approver for `> 500,000` (Step 2) |
| ------------------------- | --------------------------------------- | --------------------------------------- | ---------------------------------------- |
| `Manager`                 | Allowed at `<= 50,000`                  | Allowed                                 | Not allowed                              |
| `Approver` (legacy alias) | Same as Manager: allowed at `<= 50,000` | Allowed                                 | Not allowed                              |
| `Director`                | Allowed at `<= 500,000`                 | Allowed                                 | Allowed and required                     |
| `Admin`                   | Not authorized at any amount            | Not allowed                             | Not allowed                              |
| Any other role            | Not authorized at any amount            | Not allowed                             | Not allowed                              |

- `Admin` and unsupported roles must be rejected with `UNAUTHORIZED_ROLE: Role <role> is not authorized to approve invoices`, with zero invoice-state changes.
- **Dual-approver rule (Step 2, not Step 1):** an invoice with an amount of `> 500,000` requires TWO distinct approvers, and the second approver MUST be a Director. During Step 1, no single approval above 500,000 succeeds.
- Apply the thresholds inclusively as written. Do not treat 50,000 or 500,000 as above its tier's limit.

### Strict Domain Invariants

- **Distinct approvers:** the same `userId` must never count as both approvers for one invoice. Compare stable user identifiers, not display names or session instances.
- **Demo identity mapping:** use `UserSession.Username.Trim().ToLowerInvariant()` as the stable `UserId` in approval history. Compare normalized IDs case-insensitively so capitalization or surrounding whitespace cannot bypass duplicate detection.
- **Atomic rejection and immutability:** if an approval attempt is rejected, return a clear, descriptive error reason. Do not modify any invoice state: status, approver history, and timestamps must remain exactly as they were before the attempt. Validate all applicable conditions before mutating or persisting invoice state.
- Keep these rules in `ApprovalService` and the domain model as appropriate; do not implement business rules in controllers or the UI.

### Repository Layers and Exact Paths

- Domain entities and lifecycle state: `backend/src/InvoiceApp.Domain/Invoice.cs`, `backend/src/InvoiceApp.Domain/ApprovalRecord.cs`, and `backend/src/InvoiceApp.Domain/InvoiceStatus.cs`.
- Approval business rules and service: `backend/src/InvoiceApp.Domain/ApprovalService.cs`.
- Repository contract: `backend/src/InvoiceApp.Domain/IInvoiceRepository.cs`; in-memory implementation: `backend/src/InvoiceApp.Api/InMemoryInvoiceRepository.cs`.
- HTTP approval and invoice routes: `backend/src/InvoiceApp.Api/Controllers/ApprovalsController.cs` and `backend/src/InvoiceApp.Api/Controllers/InvoicesController.cs`. Keep authorization and request/response mapping here; approval policy belongs in Domain.
- Approval policy failures use `ApprovalRejectedException` in Domain and map to HTTP 409 with `{ "errorCode": "...", "error": "..." }` in `ApiControllerBase`.
- Static UI components and behavior: `backend/src/InvoiceApp.Api/wwwroot/index.html`, `backend/src/InvoiceApp.Api/wwwroot/app.js`, and `backend/src/InvoiceApp.Api/wwwroot/styles.css`.
- Unit tests: `backend/tests/InvoiceApp.Tests/ApprovalServiceTests.cs`, with `backend/tests/InvoiceApp.Tests/FakeInvoiceRepository.cs` for repository setup. Add or extend tests in the existing xUnit project `backend/tests/InvoiceApp.Tests/`.
- Playwright E2E tests: `e2e/tests/`; page objects: `e2e/pages/`; runner configuration: `e2e/playwright.config.ts`. Use role-based locators (`getByRole`, `getByLabel`), not CSS or XPath selectors.

### Required Tests and Execution Commands

Use the repository's existing runners and package managers:

- Unit tests (xUnit, from the repository root): `dotnet test backend/InvoiceApp.sln`
- Playwright E2E tests (npm package in `e2e/`): `cd e2e && npm test`
- C# mutation testing uses the pinned Stryker.NET global tool. Install it with `dotnet tool install -g dotnet-stryker --version 4.13.0`, then run from the unit-test project directory:
  `cd backend/tests/InvoiceApp.Tests && dotnet stryker --project InvoiceApp.Domain.csproj --test-project InvoiceApp.Tests.csproj --mutate '**/ApprovalService.cs' --concurrency 2 --reporter ClearTextTree --reporter Json --output /tmp/stryker-approval-service`
  The root npm Stryker/Vitest runner does not mutate this C# domain.

For approval-limit changes, include boundary tests at **49,999**, **50,000**, **50,001**, **499,999**, **500,000**, and **500,001**. Also test the negative invariants: a repeated `userId` cannot satisfy the two-approver requirement; a non-Director cannot be the required second approver for an invoice above 500,000; and every rejected attempt returns a descriptive reason while leaving status, approver history, and timestamps unchanged.

### PR and Code Review Expectations

Review agents must report findings and their resolution in a Markdown table with these columns: **File/Line Reference**, **Finding & Risk**, **Actionable Fix**, and **Verdict**. Each verdict must be exactly one of **Fixed**, **Dismissed with reason**, or **Deferred with reason**; include the reason in the corresponding finding or fix details for dismissed and deferred items.
