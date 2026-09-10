namespace TicketShield.Domain.Exceptions;

public class ForbiddenAccessException : DomainException
{
    public ForbiddenAccessException(string message = "Bạn không có quyền truy cập vào tài nguyên này.") : base(message)
    {
    }
}
