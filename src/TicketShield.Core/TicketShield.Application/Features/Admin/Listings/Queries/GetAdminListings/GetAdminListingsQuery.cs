using MediatR;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.Listings.Models;
using TicketShield.Domain.Enums;

namespace TicketShield.Application.Features.Admin.Listings.Queries.GetAdminListings;

/// <summary>
/// MediatR Query tra cứu danh sách tin bán lẻ & gói Combo, thông tin người bán, giá niêm yết và trạng thái kiểm soát trần giá.
/// Đáp ứng yêu cầu SCRUM-333 / SCRUM-336 (BE-11.1.3)
/// </summary>
public class GetAdminListingsQuery : IRequest<ApiResponse<AdminListingsResponseDto>>
{
    public int Page { get; set; } = 1;
    public int Size { get; set; } = 20;
    public ListingStatus? Status { get; set; }
    public VerificationStatus? VerificationStatus { get; set; }
    public bool? IsBundleOnly { get; set; }
    public bool? IsSingleOnly { get; set; }
    public bool? IsAtPriceCeilingOnly { get; set; }
    public Guid? EventId { get; set; }
    public Guid? SellerId { get; set; }
    public string? Keyword { get; set; }
    public DateTimeOffset? FromDate { get; set; }
    public DateTimeOffset? ToDate { get; set; }

    public GetAdminListingsQuery() { }

    public GetAdminListingsQuery(
        int page = 1,
        int size = 20,
        ListingStatus? status = null,
        VerificationStatus? verificationStatus = null,
        bool? isBundleOnly = null,
        bool? isSingleOnly = null,
        bool? isAtPriceCeilingOnly = null,
        Guid? eventId = null,
        Guid? sellerId = null,
        string? keyword = null,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null)
    {
        Page = page > 0 ? page : 1;
        Size = size > 0 ? size : 20;
        Status = status;
        VerificationStatus = verificationStatus;
        IsBundleOnly = isBundleOnly;
        IsSingleOnly = isSingleOnly;
        IsAtPriceCeilingOnly = isAtPriceCeilingOnly;
        EventId = eventId;
        SellerId = sellerId;
        Keyword = keyword;
        FromDate = fromDate;
        ToDate = toDate;
    }
}

