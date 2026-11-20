namespace TicketShield.Domain.Exceptions;

public class DisputeDeadlineExpiredException : BusinessRuleViolationException
{
    public DisputeDeadlineExpiredException()
        : base("The dispute window has expired.")
    {
    }
}
