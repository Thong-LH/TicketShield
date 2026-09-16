namespace TicketShield.Application.Common.Interfaces;

public interface IEmailTemplateService
{
    string GetBuyerTicketIssuedEmailHtml(
        string buyerName,
        string eventName,
        string eventDate,
        string venue,
        string ticketTier,
        string seatNumber,
        string ticketCode,
        string qrCodeDataUrl,
        string escrowCode,
        decimal amountPaid);

    string GetSellerEscrowLockedEmailHtml(
        string sellerName,
        string buyerName,
        string listingTitle,
        string eventName,
        decimal amountLocked,
        string escrowCode);
}
