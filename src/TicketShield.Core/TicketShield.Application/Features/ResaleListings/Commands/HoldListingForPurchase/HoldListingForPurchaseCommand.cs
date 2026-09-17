using MediatR;
using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.ResaleListings.Commands.HoldListingForPurchase;

public class HoldListingForPurchaseCommand : IRequest<ApiResponse<HoldListingForPurchaseResponse>>
{
    public Guid ListingId { get; set; }
    public string? PrivateAccessToken { get; set; }
    public string? RecipientName { get; set; }
    public string? RecipientEmail { get; set; }
    public string? RecipientIdCard { get; set; }

    public HoldListingForPurchaseCommand() { }

    public HoldListingForPurchaseCommand(
        Guid listingId,
        string? privateAccessToken = null,
        string? recipientName = null,
        string? recipientEmail = null,
        string? recipientIdCard = null)
    {
        ListingId = listingId;
        PrivateAccessToken = privateAccessToken;
        RecipientName = recipientName;
        RecipientEmail = recipientEmail;
        RecipientIdCard = recipientIdCard;
    }
}
