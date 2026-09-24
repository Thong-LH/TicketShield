using System.Text.Json.Serialization;
using MediatR;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Application.Features.Auth.Models;

namespace TicketShield.Identity.Application.Features.Auth.Commands.UpdateProfile;

public record UpdateProfileCommand(
    string FullName,
    string? PhoneNumber = null,
    string? IdCardNumber = null
) : IRequest<ApiResponse<UserProfileDto>>
{
    [JsonIgnore]
    public Guid? UserId { get; init; }
}
