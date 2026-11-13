using MediatR;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.Analytics.Models;

namespace TicketShield.Application.Features.Admin.Analytics.Queries.GetAdminPlatformAnalytics;

/// <summary>
/// MediatR Query thống kê tổng doanh thu thực tế của sàn, tổng số người dùng (Buyer vs Seller), số lượng vé giao dịch theo từng sự kiện và kho vé Admin.
/// Đáp ứng yêu cầu SCRUM-333 / SCRUM-338 (BE-11.1.5)
/// </summary>
public class GetAdminPlatformAnalyticsQuery : IRequest<ApiResponse<AdminPlatformAnalyticsResponseDto>>
{
    public DateTimeOffset? FromDate { get; set; }
    public DateTimeOffset? ToDate { get; set; }

    public GetAdminPlatformAnalyticsQuery() { }

    public GetAdminPlatformAnalyticsQuery(DateTimeOffset? fromDate, DateTimeOffset? toDate)
    {
        FromDate = fromDate;
        ToDate = toDate;
    }
}

