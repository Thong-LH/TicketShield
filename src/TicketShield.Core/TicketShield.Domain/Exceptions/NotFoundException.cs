namespace TicketShield.Domain.Exceptions;

public class NotFoundException : DomainException
{
    public NotFoundException(string message) : base(message)
    {
    }

    public NotFoundException(string entityName, object key)
        : base($"Entity '{entityName}' with identifier '{key}' was not found.")
    {
    }
}
