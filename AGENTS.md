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
- Every endpoint is behind sign-in. Restrict actions with `[RequireRole("...")]`: destructive actions are Admin only, approvals are Approver or Admin.
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
- `demo.admin` / `ChangeMe123!` (role Admin)

## Never

- Commit real credentials, tokens or customer data.
- Add business logic to `Program.cs` or controllers.

## Feature Context: Invoice Approval Limits by Role

This section defines the intended approval policy and implementation guidance for invoice approval limits. Preserve the existing layering: enforce approval rules in the Domain project, and keep controllers focused on HTTP concerns.

### Approval Tier Thresholds

- **Manager:** may approve an invoice with an amount of `<= 50,000`.
- **Director:** may approve an invoice with an amount of `<= 500,000`.
- **Dual-approver rule:** an invoice with an amount of `> 500,000` requires two distinct approvers. The second approver MUST be a Director.
- Apply the thresholds inclusively as written. Do not treat 50,000 or 500,000 as above its tier's limit.

### Strict Domain Invariants

- **Distinct approvers:** the same `userId` must never count as both approvers for one invoice. Compare stable user identifiers, not display names or session instances.
- **Atomic rejection and immutability:** if an approval attempt is rejected, return a clear, descriptive error reason. Do not modify any invoice state: status, approver history, and timestamps must remain exactly as they were before the attempt. Validate all applicable conditions before mutating or persisting invoice state.
- Keep these rules in `ApprovalService` and the domain model as appropriate; do not implement business rules in controllers or the UI.

### Repository Layers and Exact Paths

- Domain entities and lifecycle state: `backend/src/InvoiceApp.Domain/Invoice.cs` and `backend/src/InvoiceApp.Domain/InvoiceStatus.cs`.
- Approval business rules and service: `backend/src/InvoiceApp.Domain/ApprovalService.cs`.
- Repository contract: `backend/src/InvoiceApp.Domain/IInvoiceRepository.cs`; in-memory implementation: `backend/src/InvoiceApp.Api/InMemoryInvoiceRepository.cs`.
- HTTP approval and invoice routes: `backend/src/InvoiceApp.Api/Controllers/ApprovalsController.cs` and `backend/src/InvoiceApp.Api/Controllers/InvoicesController.cs`. Keep authorization and request/response mapping here; approval policy belongs in Domain.
- Static UI components and behavior: `backend/src/InvoiceApp.Api/wwwroot/index.html`, `backend/src/InvoiceApp.Api/wwwroot/app.js`, and `backend/src/InvoiceApp.Api/wwwroot/styles.css`.
- Unit tests: `backend/tests/InvoiceApp.Tests/ApprovalServiceTests.cs`, with `backend/tests/InvoiceApp.Tests/FakeInvoiceRepository.cs` for repository setup. Add or extend tests in the existing xUnit project `backend/tests/InvoiceApp.Tests/`.
- Playwright E2E tests: `e2e/tests/`; page objects: `e2e/pages/`; runner configuration: `e2e/playwright.config.ts`. Use role-based locators (`getByRole`, `getByLabel`), not CSS or XPath selectors.

### Required Tests and Execution Commands

Use the repository's existing runners and package managers:

- Unit tests (xUnit, from the repository root): `dotnet test backend/InvoiceApp.sln`
- Playwright E2E tests (npm package in `e2e/`): `cd e2e && npm test`
- Stryker CLI invocation (npm package at the repository root): `npx stryker run`. This repository currently has no Stryker configuration and no .NET Stryker runner; Stryker mutation testing of the C# domain is therefore not configured/runnable until the appropriate runner and configuration are added. The root npm dependencies include Stryker Core and its Vitest runner, which do not by themselves configure mutation testing for this .NET project.

For approval-limit changes, include boundary tests at **49,999**, **50,000**, **50,001**, **499,999**, **500,000**, and **500,001**. Also test the negative invariants: a repeated `userId` cannot satisfy the two-approver requirement; a non-Director cannot be the required second approver for an invoice above 500,000; and every rejected attempt returns a descriptive reason while leaving status, approver history, and timestamps unchanged.

### PR and Code Review Expectations

Review agents must report findings and their resolution in a Markdown table with these columns: **File/Line Reference**, **Finding & Risk**, **Actionable Fix**, and **Verdict**. Each verdict must be exactly one of **Fixed**, **Dismissed with reason**, or **Deferred with reason**; include the reason in the corresponding finding or fix details for dismissed and deferred items.
