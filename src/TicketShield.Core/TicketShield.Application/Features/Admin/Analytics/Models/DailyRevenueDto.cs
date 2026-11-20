namespace TicketShield.Application.Features.Admin.Analytics.Models;

/// <summary>
/// Platform revenue of one day for the Admin Dashboard trend chart (SCRUM-391 / SCRUM-393).
/// Only Released escrows count, grouped by their creation day in Vietnam time (UTC+7).
/// </summary>
public class DailyRevenueDto
{
    /// <summary>
    /// Day in Vietnam time, formatted yyyy-MM-dd.
    /// </summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>
    /// Buyer fee plus seller fee of the Released escrows created that day.
    /// </summary>
    public decimal NetRevenue { get; set; }

    /// <summary>
    /// Total paid by buyers on the Released escrows created that day.
    /// </summary>
    public decimal Gmv { get; set; }
}
