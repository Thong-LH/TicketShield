using System.Net;

namespace MockOrganizer.API.Gate;

public interface ITicketUsedNotifier
{
    Task NotifyAsync(string ticketCode, CancellationToken cancellationToken);
}

public sealed class HttpTicketUsedNotifier(
    HttpClient http,
    IConfiguration configuration,
    ILogger<HttpTicketUsedNotifier> logger) : ITicketUsedNotifier
{
    private const string WebhookPath = "api/v1/webhooks/organizer/ticket-used";

    public async Task NotifyAsync(string ticketCode, CancellationToken cancellationToken)
    {
        var secret = configuration["CoreWebhook:Secret"] ?? configuration["OrganizerResale:ApiKey"];

        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath)
        {
            Content = JsonContent.Create(new { ticketCode })
        };
        request.Headers.Add("X-Organizer-Secret", secret);

        using var response = await http.SendAsync(request, cancellationToken);

        // Vé mua thẳng từ BTC không có giao dịch ký quỹ bên Core, đây không phải lỗi.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogInformation("Ticket {TicketCode} has no escrow in Core.", ticketCode);
            return;
        }

        response.EnsureSuccessStatusCode();
    }
}
