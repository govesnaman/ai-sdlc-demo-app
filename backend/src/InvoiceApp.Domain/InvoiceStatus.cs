namespace InvoiceApp.Domain;

public enum InvoiceStatus
{
    Draft,
    PendingApproval,
    PartiallyApproved,
    Approved,
    Rejected
}
