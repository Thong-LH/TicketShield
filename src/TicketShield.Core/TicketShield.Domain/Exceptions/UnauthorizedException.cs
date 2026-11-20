namespace TicketShield.Domain.Exceptions;

public class UnauthorizedException : DomainException
{
    public UnauthorizedException(string message = "You are not authenticated or your session has expired. Please sign in again.")
        : base(message)
    {
    }
}
