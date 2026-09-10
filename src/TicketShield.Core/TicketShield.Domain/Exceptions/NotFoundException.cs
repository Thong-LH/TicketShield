namespace TicketShield.Domain.Exceptions;

public class NotFoundException : DomainException
{
    public NotFoundException(string message) : base(message)
    {
    }

    public NotFoundException(string entityName, object key)
        : base($"Không tìm thấy {entityName} với mã định danh '{key}'.")
    {
    }
}
