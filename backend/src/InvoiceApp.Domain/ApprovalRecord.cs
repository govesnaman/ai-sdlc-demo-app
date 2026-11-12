namespace InvoiceApp.Domain;

public class ApprovalRecord
{
    public string UserId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime ApprovedAt { get; set; }
}
