using System.Globalization;

namespace InvoiceApp.Domain;

/// <summary>
/// Moves invoices through their lifecycle and enforces approval limits.
/// </summary>
public class ApprovalService
{
    private const decimal ManagerApprovalLimit = 50_000m;
    private const decimal DirectorApprovalLimit = 500_000m;
    private readonly IInvoiceRepository _repository;

    public ApprovalService(IInvoiceRepository repository)
    {
        _repository = repository;
    }

    public Invoice Submit(string invoiceId)
    {
        var invoice = Find(invoiceId);

        if (invoice.Status != InvoiceStatus.Draft)
        {
            throw new InvalidOperationException(
                $"Invoice {invoiceId} is {invoice.Status}; only drafts can be submitted.");
        }

        invoice.Status = InvoiceStatus.PendingApproval;
        _repository.Update(invoice);
        return invoice;
    }

    public Invoice Approve(string invoiceId, string approver, string role)
    {
        var approvalTier = role switch
        {
            "Manager" or "Approver" => "Manager",
            "Director" => "Director",
            _ => throw new ApprovalRejectedException(
                "UNAUTHORIZED_ROLE",
                $"Role {role} is not authorized to approve invoices")
        };

        var invoice = Find(invoiceId);
        EnsurePending(invoice);

        var approvalLimit = approvalTier == "Manager"
            ? ManagerApprovalLimit
            : DirectorApprovalLimit;

        if (invoice.Amount > approvalLimit)
        {
            var formattedAmount = invoice.Amount.ToString(
                "#,##0.############################",
                CultureInfo.InvariantCulture);
            var reason = approvalTier == "Manager"
                ? $"Manager approval limit is 50,000; invoice amount is {formattedAmount}"
                : $"Director single-approver limit is 500,000; invoice amount is {formattedAmount}. Dual-approver approval is not implemented yet";
            throw new ApprovalRejectedException("APPROVAL_LIMIT_EXCEEDED", reason);
        }

        invoice.Status = InvoiceStatus.Approved;
        invoice.DecidedBy = approver;
        invoice.DecidedAt = DateTime.UtcNow;
        _repository.Update(invoice);
        return invoice;
    }

    public Invoice Reject(string invoiceId, string approver, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required to reject an invoice.", nameof(reason));
        }

        var invoice = Find(invoiceId);
        EnsurePending(invoice);

        invoice.Status = InvoiceStatus.Rejected;
        invoice.DecidedBy = approver;
        invoice.DecidedAt = DateTime.UtcNow;
        invoice.DecisionNote = reason;
        _repository.Update(invoice);
        return invoice;
    }

    private Invoice Find(string invoiceId) =>
        _repository.GetById(invoiceId)
        ?? throw new KeyNotFoundException($"Invoice {invoiceId} was not found.");

    private static void EnsurePending(Invoice invoice)
    {
        if (invoice.Status != InvoiceStatus.PendingApproval)
        {
            throw new InvalidOperationException(
                $"Invoice {invoice.Id} is {invoice.Status}; only pending invoices can be decided.");
        }
    }
}
