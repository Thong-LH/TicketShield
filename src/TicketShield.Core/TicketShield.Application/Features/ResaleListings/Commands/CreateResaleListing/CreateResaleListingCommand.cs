using MediatR;
using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.ResaleListings.Commands.CreateResaleListing;

public class CreateResaleListingCommand : IRequest<ApiResponse<CreateResaleListingResponse>>
{
    public Guid EventId { get; set; }
    public Guid TierId { get; set; }
    public Guid? SellerId { get; set; }
    public string OriginalTicketCode { get; set; } = string.Empty;
    public decimal OriginalPrice { get; set; }
    public decimal ResalePrice { get; set; }
    public bool IsPrivate { get; set; } = false;
}
