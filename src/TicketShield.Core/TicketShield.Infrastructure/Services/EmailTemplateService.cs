using TicketShield.Application.Common.Interfaces;

namespace TicketShield.Infrastructure.Services;

public class EmailTemplateService : IEmailTemplateService
{
    private readonly string _templateBasePath;

    public EmailTemplateService()
    {
        var baseDir = AppContext.BaseDirectory;
        var potentialPaths = new[]
        {
            Path.Combine(baseDir, "Templates"),
            Path.Combine(baseDir, "..", "..", "..", "..", "TicketShield.Infrastructure", "Templates"),
            Path.Combine(Directory.GetCurrentDirectory(), "Templates"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "TicketShield.Infrastructure", "Templates")
        };

        _templateBasePath = potentialPaths.FirstOrDefault(Directory.Exists)
            ?? Path.Combine(baseDir, "Templates");
    }

    public string GetBuyerTicketIssuedEmailHtml(
        string buyerName,
        string eventName,
        string eventDate,
        string venue,
        string ticketTier,
        string seatNumber,
        string ticketCode,
        string qrCodeDataUrl,
        string escrowCode,
        decimal amountPaid)
    {
        var templatePath = Path.Combine(_templateBasePath, "buyer_ticket_issued.html");
        var html = LoadTemplate(templatePath);

        return html
            .Replace("{{BuyerName}}", buyerName)
            .Replace("{{EventName}}", eventName)
            .Replace("{{EventDate}}", eventDate)
            .Replace("{{Venue}}", venue)
            .Replace("{{TicketTier}}", ticketTier)
            .Replace("{{SeatNumber}}", seatNumber)
            .Replace("{{TicketCode}}", ticketCode)
            .Replace("{{QrCodeDataUrl}}", qrCodeDataUrl)
            .Replace("{{EscrowCode}}", escrowCode)
            .Replace("{{AmountPaid}}", amountPaid.ToString("N0"));
    }

    public string GetSellerEscrowLockedEmailHtml(
        string sellerName,
        string buyerName,
        string listingTitle,
        string eventName,
        decimal amountLocked,
        string escrowCode)
    {
        var templatePath = Path.Combine(_templateBasePath, "seller_escrow_locked.html");
        var html = LoadTemplate(templatePath);

        return html
            .Replace("{{SellerName}}", sellerName)
            .Replace("{{BuyerName}}", buyerName)
            .Replace("{{ListingTitle}}", listingTitle)
            .Replace("{{EventName}}", eventName)
            .Replace("{{AmountLocked}}", amountLocked.ToString("N0"))
            .Replace("{{EscrowCode}}", escrowCode);
    }

    private static string LoadTemplate(string templatePath)
    {
        if (File.Exists(templatePath))
        {
            return File.ReadAllText(templatePath);
        }

        throw new FileNotFoundException($"Email HTML template not found at path: {templatePath}");
    }
}
