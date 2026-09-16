namespace TicketShield.Identity.Domain.Exceptions;

public class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException(string message = "Bạn không có quyền truy cập vào tài nguyên này.")
        : base(message)
    {
    }
}
