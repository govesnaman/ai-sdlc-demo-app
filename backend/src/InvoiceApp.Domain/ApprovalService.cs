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
        var invoice = Find(invoiceId);

        if (invoice.Status == InvoiceStatus.Approved)
        {
            throw new ApprovalRejectedException(
                "ALREADY_APPROVED",
                $"Invoice {invoice.Id} has already been approved.");
        }

        if (string.IsNullOrWhiteSpace(approver))
        {
            throw new ApprovalRejectedException(
                "INVALID_APPROVER_ID",
                "An approver username is required.");
        }

        var approverId = approver.Trim().ToLowerInvariant();
        if (invoice.Status == InvoiceStatus.PartiallyApproved &&
            invoice.Amount > DirectorApprovalLimit &&
            invoice.Approvals.Any(existing =>
                string.Equals(
                    existing.UserId.Trim(),
                    approverId,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new ApprovalRejectedException(
                "DUPLICATE_APPROVER",
                "The same user cannot approve the same invoice twice");
        }

        var approvalTier = role switch
        {
            "Manager" or "Approver" => "Manager",
            "Director" => "Director",
            _ => throw new ApprovalRejectedException(
                "UNAUTHORIZED_ROLE",
                $"Role {role} is not authorized to approve invoices")
        };

        var approval = new ApprovalRecord
        {
            UserId = approverId,
            Role = role,
            ApprovedAt = DateTime.UtcNow
        };

        if (invoice.Status == InvoiceStatus.PartiallyApproved)
        {
            if (invoice.Amount <= DirectorApprovalLimit || invoice.Approvals.Count != 1)
            {
                throw InvalidApprovalState(invoice);
            }

            if (role != "Director")
            {
                throw new ApprovalRejectedException(
                    "INVALID_SECOND_APPROVER",
                    "The second approver for invoices above 500,000 must be a Director");
            }

            invoice.Approvals.Add(approval);
            invoice.Status = InvoiceStatus.Approved;
            SetFinalApproval(invoice, approval);
            _repository.Update(invoice);
            return invoice;
        }

        if (invoice.Status == InvoiceStatus.PendingApproval && invoice.Amount <= DirectorApprovalLimit)
        {
            var approvalLimit = approvalTier == "Manager"
                ? ManagerApprovalLimit
                : DirectorApprovalLimit;

            if (invoice.Amount > approvalLimit)
            {
                var formattedAmount = invoice.Amount.ToString(
                    "#,##0.############################",
                    CultureInfo.InvariantCulture);
                var reason = $"Manager approval limit is 50,000; invoice amount is {formattedAmount}";
                throw new ApprovalRejectedException("APPROVAL_LIMIT_EXCEEDED", reason);
            }

            if (invoice.Approvals.Count != 0)
            {
                throw InvalidApprovalState(invoice);
            }

            invoice.Approvals.Add(approval);
            invoice.Status = InvoiceStatus.Approved;
            SetFinalApproval(invoice, approval);
            _repository.Update(invoice);
            return invoice;
        }

        if (invoice.Amount > DirectorApprovalLimit && invoice.Status == InvoiceStatus.PendingApproval)
        {
            if (invoice.Approvals.Count != 0)
            {
                throw InvalidApprovalState(invoice);
            }

            invoice.Approvals.Add(approval);
            invoice.Status = InvoiceStatus.PartiallyApproved;
            _repository.Update(invoice);
            return invoice;
        }

        throw new ApprovalRejectedException(
            "INVALID_INVOICE_STATUS",
            $"Invoice {invoice.Id} is {invoice.Status}; only pending or partially approved invoices can be approved.");
    }

    public Invoice Reject(string invoiceId, string approver, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required to reject an invoice.", nameof(reason));
        }

        var invoice = Find(invoiceId);
        EnsureCanBeRejected(invoice);

        invoice.Status = InvoiceStatus.Rejected;
        invoice.DecidedBy = approver;
        invoice.DecidedAt = DateTime.UtcNow;
        invoice.DecisionNote = reason;
        _repository.Update(invoice);
        return invoice;
    }

    private static void SetFinalApproval(Invoice invoice, ApprovalRecord approval)
    {
        invoice.DecidedBy = approval.UserId;
        invoice.DecidedAt = approval.ApprovedAt;
    }

    private Invoice Find(string invoiceId) =>
        _repository.GetById(invoiceId)
        ?? throw new KeyNotFoundException($"Invoice {invoiceId} was not found.");

    private static ApprovalRejectedException InvalidApprovalState(Invoice invoice) =>
        new(
            "INVALID_APPROVAL_STATE",
            $"Invoice {invoice.Id} has an inconsistent status or approval history.");

    private static void EnsureCanBeRejected(Invoice invoice)
    {
        if (invoice.Status is not InvoiceStatus.PendingApproval and not InvoiceStatus.PartiallyApproved)
        {
            throw new InvalidOperationException(
                $"Invoice {invoice.Id} is {invoice.Status}; only pending or partially approved invoices can be decided.");
        }
    }
}
