namespace TicketShield.Identity.Domain.Exceptions;

public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message = "Yêu cầu xác thực tài khoản không hợp lệ hoặc phiên làm việc đã hết hạn.")
        : base(message)
    {
    }
}
