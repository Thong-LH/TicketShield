namespace TicketShield.Identity.Domain.Exceptions;

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }

    public NotFoundException(string name, object key)
        : base($"Không tìm thấy {name} với mã định danh '{key}'.")
    {
    }
}
