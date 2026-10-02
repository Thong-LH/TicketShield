namespace TicketShield.Application.Features.Organizers.Queries.GetOrganizers;

/// <summary>
/// DTO thông tin đối tác Ban tổ chức phục vụ người dùng chọn đơn vị phát hành vé và lọc sàn giao dịch.
/// </summary>
public sealed record OrganizerDto(
    Guid Id,
    string Name,
    string? Code,
    string? LogoUrl,
    string? OfficialEmail,
    string Status
);
