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

    private static InvoiceSnapshot Capture(Invoice invoice) =>
        new(
            invoice.Id,
            invoice.CustomerName,
            invoice.Amount,
            invoice.Status,
            invoice.CreatedAt,
            invoice.DecidedBy,
            invoice.DecidedAt,
            invoice.DecisionNote);

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
    public void Approve_DirectorAboveSingleApproverLimit_RejectsWithoutChangingInvoice()
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 500_001m);
        invoice.DecidedBy = "previous-approver";
        invoice.DecidedAt = OriginalDecidedAt;
        invoice.DecisionNote = "existing note";
        var originalState = Capture(invoice);
        var (service, repository) = ServiceFor(invoice);

        var exception = Assert.Throws<ApprovalRejectedException>(
            () => service.Approve("INV-1", "director-1", "Director"));

        Assert.Equal("APPROVAL_LIMIT_EXCEEDED", exception.ErrorCode);
        Assert.Equal(
            "APPROVAL_LIMIT_EXCEEDED: Director single-approver limit is 500,000; invoice amount is 500,001. Dual-approver approval is not implemented yet",
            exception.Message);
        Assert.Equal(originalState, Capture(invoice));
        Assert.Equal(0, repository.UpdateCount);
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

    [Fact]
    public void Approve_RejectsInvoiceThatIsStillADraft()
    {
        var (service, _) = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.Draft));

        Assert.Throws<InvalidOperationException>(() => service.Approve("INV-1", "manager-1", "Manager"));
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
        Assert.Equal(1, repository.UpdateCount);
    }

    private sealed record InvoiceSnapshot(
        string Id,
        string CustomerName,
        decimal Amount,
        InvoiceStatus Status,
        DateTime CreatedAt,
        string? DecidedBy,
        DateTime? DecidedAt,
        string? DecisionNote);
}
