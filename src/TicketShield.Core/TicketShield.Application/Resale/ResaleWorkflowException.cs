namespace TicketShield.Application.Resale;

/// <summary>
/// Ngoại lệ nghiệp vụ xảy ra trong quy trình xác thực vé và quản lý phiên bán lại
/// </summary>
public sealed class ResaleWorkflowException(string code, int httpStatus = 409) : Exception(code)
{
    public string Code { get; } = code;
    public int HttpStatus { get; } = httpStatus;
}
