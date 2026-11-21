namespace TicketShield.Domain.Exceptions;

public class ForbiddenAccessException : DomainException
{
    public ForbiddenAccessException(string message = "You do not have permission to access this resource.") : base(message)
    {
    }
}
