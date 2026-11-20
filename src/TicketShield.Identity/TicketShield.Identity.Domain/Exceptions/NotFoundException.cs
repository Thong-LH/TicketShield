namespace TicketShield.Identity.Domain.Exceptions;

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }

    public NotFoundException(string name, object key)
        : base($"Entity '{name}' with identifier '{key}' was not found.")
    {
    }
}
