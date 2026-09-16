namespace TicketShield.Application.Features.Admin.EventMarkup.Models;

public class EventResaleMarkupDto
{
    public Guid EventId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public decimal MaxResaleMarkupPercentage { get; set; }
}
