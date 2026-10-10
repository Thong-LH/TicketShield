namespace TicketShield.Domain.Exceptions;

public class DisputeDeadlineExpiredException : BusinessRuleViolationException
{
    public DisputeDeadlineExpiredException()
        : base("Thời hạn khiếu nại đã kết thúc.")
    {
    }
}
