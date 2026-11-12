using System.Text.Json;
using InvoiceApp.Domain;
using Xunit;

namespace InvoiceApp.Tests;

public class ApprovalServiceTests
{
    private static readonly DateTime OriginalCreatedAt = new(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    private static readonly DateTime OriginalDecidedAt = new(2025, 1, 3, 4, 5, 6, DateTimeKind.Utc);

    private static Invoice InvoiceWith(string id, InvoiceStatus status, decimal amount = 100m) =>
        new()
        {
            Id = id,
            CustomerName = "Test School",
            Amount = amount,
            Status = status,
            CreatedAt = OriginalCreatedAt
        };

    private static (ApprovalService Service, FakeInvoiceRepository Repository) ServiceFor(params Invoice[] invoices)
    {
        var repository = new FakeInvoiceRepository(invoices);
        return (new ApprovalService(repository), repository);
    }

    private static string Capture(Invoice invoice) => JsonSerializer.Serialize(invoice);

    [Fact]
    public void Submit_MovesDraftToPendingApproval()
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.Draft);

        var (service, repository) = ServiceFor(invoice);
        var result = service.Submit("INV-1");

        Assert.Equal(InvoiceStatus.PendingApproval, result.Status);
        Assert.Equal(1, repository.UpdateCount);
    }

    [Fact]
    public void Submit_RejectsInvoiceThatIsNotADraft()
    {
        var (service, _) = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.Approved));

        Assert.Throws<InvalidOperationException>(() => service.Submit("INV-1"));
    }

    [Theory]
    [InlineData(49_999)]
    [InlineData(50_000)]
    public void Approve_Manager_SucceedsAtAndBelowLimit(decimal amount)
    {
        AssertApprovalSucceeds(amount, "Manager", "manager-1");
    }

    [Theory]
    [InlineData(49_999)]
    [InlineData(50_000)]
    [InlineData(50_001)]
    [InlineData(499_999)]
    [InlineData(500_000)]
    public void Approve_Director_SucceedsAtAndBelowLimit(decimal amount)
    {
        AssertApprovalSucceeds(amount, "Director", "director-1");
    }

    [Theory]
    [InlineData(49_999)]
    [InlineData(50_000)]
    public void Approve_LegacyApproverAlias_UsesManagerLimit(decimal amount)
    {
        AssertApprovalSucceeds(amount, "Approver", "legacy-approver");
    }

    [Fact]
    public void Approve_ManagerAboveLimit_RejectsWithoutChangingInvoice()
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 50_001m);
        invoice.DecidedBy = "previous-approver";
        invoice.DecidedAt = OriginalDecidedAt;
        invoice.DecisionNote = "existing note";
        var originalState = Capture(invoice);
        var (service, repository) = ServiceFor(invoice);

        var exception = Assert.Throws<ApprovalRejectedException>(
            () => service.Approve("INV-1", "manager-1", "Manager"));

        Assert.Equal("APPROVAL_LIMIT_EXCEEDED", exception.ErrorCode);
        Assert.Equal(
            "APPROVAL_LIMIT_EXCEEDED: Manager approval limit is 50,000; invoice amount is 50,001",
            exception.Message);
        Assert.Equal(originalState, Capture(invoice));
        Assert.Equal(0, repository.UpdateCount);
    }

    [Fact]
    public void Approve_LegacyApproverAliasAboveLimit_RejectsWithoutChangingInvoice()
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 50_001m);
        var originalState = Capture(invoice);
        var (service, repository) = ServiceFor(invoice);

        var exception = Assert.Throws<ApprovalRejectedException>(
            () => service.Approve("INV-1", "legacy-approver", "Approver"));

        Assert.Equal("APPROVAL_LIMIT_EXCEEDED", exception.ErrorCode);
        Assert.Contains("Manager approval limit is 50,000", exception.Message);
        Assert.Equal(originalState, Capture(invoice));
        Assert.Equal(0, repository.UpdateCount);
    }

    [Fact]
    public void Approve_ManagerThenDirector_ApprovesInvoiceAbove500000()
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 500_001m);
        invoice.DecidedBy = "previous-approver";
        invoice.DecidedAt = OriginalDecidedAt;
        invoice.DecisionNote = "previous note";
        var (service, repository) = ServiceFor(invoice);

        var partialResult = service.Approve("INV-1", " ManagerA ", "Manager");

        Assert.Same(invoice, partialResult);
        Assert.Equal(InvoiceStatus.PartiallyApproved, invoice.Status);
        var firstApproval = Assert.Single(invoice.Approvals);
        Assert.Equal("managera", firstApproval.UserId);
        Assert.Equal("Manager", firstApproval.Role);
        Assert.NotEqual(default, firstApproval.ApprovedAt);
        Assert.Equal("previous-approver", invoice.DecidedBy);
        Assert.Equal(OriginalDecidedAt, invoice.DecidedAt);
        Assert.Equal("previous note", invoice.DecisionNote);
        Assert.Equal(1, repository.UpdateCount);

        var result = service.Approve("INV-1", "DirectorB", "Director");

        Assert.Same(invoice, result);
        Assert.Equal(InvoiceStatus.Approved, invoice.Status);
        Assert.Collection(invoice.Approvals, _ => { }, _ => { });
        Assert.Equal("directorb", invoice.Approvals[1].UserId);
        Assert.Equal("Director", invoice.Approvals[1].Role);
        Assert.Equal(invoice.Approvals[1].ApprovedAt, invoice.DecidedAt);
        Assert.Equal("directorb", invoice.DecidedBy);
        Assert.Equal(2, repository.UpdateCount);
    }

    [Fact]
    public void Approve_DirectorThenDirector_ApprovesInvoiceAbove500000()
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 750_000m);
        var (service, repository) = ServiceFor(invoice);

        service.Approve("INV-1", "director-a", "Director");

        Assert.Equal(InvoiceStatus.PartiallyApproved, invoice.Status);
        Assert.Equal("director-a", Assert.Single(invoice.Approvals).UserId);

        service.Approve("INV-1", "director-b", "Director");

        Assert.Equal(InvoiceStatus.Approved, invoice.Status);
        Assert.Collection(invoice.Approvals, _ => { }, _ => { });
        Assert.Equal(2, repository.UpdateCount);
    }

    [Fact]
    public void Approve_LegacyApproverCanBeFirstApproverAbove500000()
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 600_000m);
        var (service, _) = ServiceFor(invoice);

        service.Approve("INV-1", "legacy-approver", "Approver");

        Assert.Equal(InvoiceStatus.PartiallyApproved, invoice.Status);
        var approval = Assert.Single(invoice.Approvals);
        Assert.Equal("legacy-approver", approval.UserId);
        Assert.Equal("Approver", approval.Role);
    }

    [Theory]
    [InlineData("Director", "DirectorA", " directora ")]
    [InlineData("Manager", "ManagerA", "managera")]
    public void Approve_DuplicateSecondApprover_RejectsWithoutChangingInvoice(
        string role,
        string firstApprover,
        string secondApprover)
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 600_000m);
        invoice.DecidedBy = "previous-approver";
        invoice.DecidedAt = OriginalDecidedAt;
        invoice.DecisionNote = "previous note";
        var (service, repository) = ServiceFor(invoice);

        service.Approve("INV-1", firstApprover, role);
        var originalState = Capture(invoice);

        var exception = Assert.Throws<ApprovalRejectedException>(
            () => service.Approve("INV-1", secondApprover, role));

        Assert.Equal("DUPLICATE_APPROVER", exception.ErrorCode);
        Assert.Equal(
            "DUPLICATE_APPROVER: The same user cannot approve the same invoice twice",
            exception.Message);
        Assert.Equal(InvoiceStatus.PartiallyApproved, invoice.Status);
        Assert.Single(invoice.Approvals);
        Assert.Equal(originalState, Capture(invoice));
        Assert.Equal(1, repository.UpdateCount);
    }

    [Fact]
    public void Approve_DuplicateApproverWithCaseAndWhitespaceVariant_RejectsWithoutChangingInvoice()
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 600_000m);
        var (service, repository) = ServiceFor(invoice);

        service.Approve("INV-1", " Director1 ", "Director");
        var originalState = Capture(invoice);

        var exception = Assert.Throws<ApprovalRejectedException>(
            () => service.Approve("INV-1", "director1", "Director"));

        Assert.Equal("DUPLICATE_APPROVER", exception.ErrorCode);
        Assert.Equal(originalState, Capture(invoice));
        Assert.Single(invoice.Approvals);
        Assert.Equal(1, repository.UpdateCount);
    }

    [Fact]
    public void Approve_DuplicateUserWithChangedRole_RejectsBeforeSecondRoleValidation()
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 600_000m);
        var (service, repository) = ServiceFor(invoice);

        service.Approve("INV-1", "manager-a", "Manager");
        var originalState = Capture(invoice);

        var exception = Assert.Throws<ApprovalRejectedException>(
            () => service.Approve("INV-1", "MANAGER-A", "Director"));

        Assert.Equal("DUPLICATE_APPROVER", exception.ErrorCode);
        Assert.Equal(originalState, Capture(invoice));
        Assert.Single(invoice.Approvals);
        Assert.Equal(1, repository.UpdateCount);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Auditor")]
    public void Approve_UnauthorizedRole_RejectsWithoutChangingInvoice(string role)
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 49_999m);
        invoice.DecidedBy = "previous-approver";
        invoice.DecidedAt = OriginalDecidedAt;
        invoice.DecisionNote = "existing note";
        var originalState = Capture(invoice);
        var (service, repository) = ServiceFor(invoice);

        var exception = Assert.Throws<ApprovalRejectedException>(
            () => service.Approve("INV-1", "user-1", role));

        Assert.Equal("UNAUTHORIZED_ROLE", exception.ErrorCode);
        Assert.Equal($"UNAUTHORIZED_ROLE: Role {role} is not authorized to approve invoices", exception.Message);
        Assert.Equal(originalState, Capture(invoice));
        Assert.Equal(0, repository.UpdateCount);
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Approver")]
    public void Approve_NonDirectorAsSecondApprover_RejectsWithoutChangingInvoice(string secondApproverRole)
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 600_000m);
        invoice.DecidedBy = "previous-approver";
        invoice.DecidedAt = OriginalDecidedAt;
        invoice.DecisionNote = "previous note";
        var (service, repository) = ServiceFor(invoice);

        service.Approve("INV-1", "manager-a", "Manager");
        var originalState = Capture(invoice);

        var exception = Assert.Throws<ApprovalRejectedException>(
            () => service.Approve("INV-1", "manager-b", secondApproverRole));

        Assert.Equal("INVALID_SECOND_APPROVER", exception.ErrorCode);
        Assert.Equal(
            "INVALID_SECOND_APPROVER: The second approver for invoices above 500,000 must be a Director",
            exception.Message);
        Assert.Equal(InvoiceStatus.PartiallyApproved, invoice.Status);
        Assert.Single(invoice.Approvals);
        Assert.Equal(originalState, Capture(invoice));
        Assert.Equal(1, repository.UpdateCount);
    }

    [Fact]
    public void Approve_AlreadyApprovedInvoice_RejectsWithoutChangingInvoice()
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 500_000m);
        var (service, repository) = ServiceFor(invoice);
        service.Approve("INV-1", "director-a", "Director");
        var originalState = Capture(invoice);

        var exception = Assert.Throws<ApprovalRejectedException>(
            () => service.Approve("INV-1", "director-b", "Director"));

        Assert.Equal("ALREADY_APPROVED", exception.ErrorCode);
        Assert.Equal(
            "ALREADY_APPROVED: Invoice INV-1 has already been approved.",
            exception.Message);
        Assert.Equal(originalState, Capture(invoice));
        Assert.Equal(1, repository.UpdateCount);
    }

    [Fact]
    public void Approve_RejectsInvoiceThatIsStillADraft()
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.Draft);
        var originalState = Capture(invoice);
        var (service, repository) = ServiceFor(invoice);

        var exception = Assert.Throws<ApprovalRejectedException>(
            () => service.Approve("INV-1", "manager-1", "Manager"));

        Assert.Equal("INVALID_INVOICE_STATUS", exception.ErrorCode);
        Assert.Equal(originalState, Capture(invoice));
        Assert.Equal(0, repository.UpdateCount);
    }

    [Fact]
    public void Approve_ThrowsWhenInvoiceDoesNotExist()
    {
        var (service, _) = ServiceFor();

        Assert.Throws<KeyNotFoundException>(() => service.Approve("INV-404", "manager-1", "Manager"));
    }

    [Fact]
    public void Reject_RecordsReasonAndApprover()
    {
        var (service, _) = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval));

        var result = service.Reject("INV-1", "demo.approver", "Duplicate invoice");

        Assert.Equal(InvoiceStatus.Rejected, result.Status);
        Assert.Equal("Duplicate invoice", result.DecisionNote);
    }

    [Fact]
    public void Reject_PartiallyApprovedInvoice_PreservesApprovalHistory()
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 600_000m);
        var (service, _) = ServiceFor(invoice);
        service.Approve("INV-1", "manager-a", "Manager");
        var originalApproval = Assert.Single(invoice.Approvals);

        var result = service.Reject("INV-1", "director-b", "Budget no longer available");

        Assert.Equal(InvoiceStatus.Rejected, result.Status);
        Assert.Same(originalApproval, Assert.Single(result.Approvals));
        Assert.Equal("Budget no longer available", result.DecisionNote);
    }

    [Fact]
    public void Reject_RequiresAReason()
    {
        var (service, _) = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval));

        Assert.Throws<ArgumentException>(() => service.Reject("INV-1", "demo.approver", "  "));
    }

    private static void AssertApprovalSucceeds(decimal amount, string role, string approver)
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.PendingApproval, amount);
        invoice.DecisionNote = "existing note";
        var (service, repository) = ServiceFor(invoice);

        var result = service.Approve("INV-1", approver, role);

        Assert.Same(invoice, result);
        Assert.Equal(InvoiceStatus.Approved, result.Status);
        Assert.Equal(approver, result.DecidedBy);
        Assert.NotNull(result.DecidedAt);
        Assert.Equal(OriginalCreatedAt, result.CreatedAt);
        Assert.Equal("existing note", result.DecisionNote);
        var approval = Assert.Single(result.Approvals);
        Assert.Equal(approver.ToLowerInvariant(), approval.UserId);
        Assert.Equal(role, approval.Role);
        Assert.Equal(approval.ApprovedAt, result.DecidedAt);
        Assert.Equal(1, repository.UpdateCount);
    }
}
