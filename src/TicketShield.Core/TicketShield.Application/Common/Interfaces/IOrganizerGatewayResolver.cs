using TicketShield.Contracts.Organizer.V1;

namespace TicketShield.Application.Common.Interfaces;

/// <summary>
/// Trình phân giải kết nối gRPC và REST động tới từng Ban tổ chức (Dynamic Organizer Gateway Resolver)
/// Đáp ứng yêu cầu US-7.5 (SCRUM-239 & BE-CORE-7.5.2)
/// </summary>
public interface IOrganizerGatewayResolver
{
    /// <summary>
    /// Phân giải gRPC Client kết nối tới Ban tổ chức tương ứng theo OrganizerId
    /// </summary>
    Task<OrganizerResaleService.OrganizerResaleServiceClient> ResolveClientAsync(
        Guid? organizerId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy API Base URL của Ban tổ chức tương ứng
    /// </summary>
    Task<string?> ResolveApiBaseUrlAsync(
        Guid organizerId,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy Webhook Secret Key của Ban tổ chức để xác thực chữ ký
    /// </summary>
    Task<string?> ResolveWebhookSecretKeyAsync(
        Guid organizerId,
        CancellationToken ct = default);

    /// <summary>
    /// Vô hiệu hóa và giải phóng gRPC Channel trong cache khi cập nhật cấu hình hoặc xoay secret
    /// </summary>
    void InvalidateChannel(string endpoint);
}
