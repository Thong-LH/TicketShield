namespace TicketShield.Identity.Domain.Exceptions;

public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message = "Authentication request is invalid or your session has expired.")
        : base(message)
    {
    }
}
