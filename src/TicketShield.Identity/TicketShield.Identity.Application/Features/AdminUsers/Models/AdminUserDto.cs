using TicketShield.Identity.Domain.Entities;

namespace TicketShield.Identity.Application.Features.AdminUsers.Models;

public class AdminUserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int TrustScore { get; set; }

    public static AdminUserDto FromUser(User user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        FullName = user.FullName,
        PhoneNumber = user.PhoneNumber,
        Role = user.Role.ToString(),
        IsActive = user.IsActive,
        TrustScore = user.TrustScore
    };
}
