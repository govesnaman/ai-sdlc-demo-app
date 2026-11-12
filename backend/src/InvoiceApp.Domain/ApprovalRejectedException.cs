namespace InvoiceApp.Domain;

public sealed class ApprovalRejectedException : InvalidOperationException
{
    public ApprovalRejectedException(string errorCode, string reason)
        : base($"{errorCode}: {reason}")
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
