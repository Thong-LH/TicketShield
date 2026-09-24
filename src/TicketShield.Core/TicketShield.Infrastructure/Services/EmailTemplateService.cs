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
        var html = LoadTemplate("buyer_ticket_issued.html");

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
        var html = LoadTemplate("seller_escrow_locked.html");

        return html
            .Replace("{{SellerName}}", sellerName)
            .Replace("{{BuyerName}}", buyerName)
            .Replace("{{ListingTitle}}", listingTitle)
            .Replace("{{EventName}}", eventName)
            .Replace("{{AmountLocked}}", amountLocked.ToString("N0"))
            .Replace("{{EscrowCode}}", escrowCode);
    }

    private string LoadTemplate(string templateFileName)
    {
        // 1. Check template base path probed at startup
        var pathOnDisk = Path.Combine(_templateBasePath, templateFileName);
        if (File.Exists(pathOnDisk))
        {
            return File.ReadAllText(pathOnDisk);
        }

        // 2. Check AppContext.BaseDirectory/Templates direct path
        var baseDirPath = Path.Combine(AppContext.BaseDirectory, "Templates", templateFileName);
        if (File.Exists(baseDirPath))
        {
            return File.ReadAllText(baseDirPath);
        }

        // 3. Fallback to Embedded Resource for containerized environments
        var assembly = typeof(EmailTemplateService).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(r => r.EndsWith(templateFileName, StringComparison.OrdinalIgnoreCase));

        if (resourceName != null)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
        }

        throw new FileNotFoundException($"Email HTML template not found at disk path '{pathOnDisk}' or embedded resource '{templateFileName}'.");
    }
}
