namespace TicketShield.Application.Common.Interfaces;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? Email { get; }
    string? FullName => null;
    string? Role { get; }
    bool IsAuthenticated { get; }
}
