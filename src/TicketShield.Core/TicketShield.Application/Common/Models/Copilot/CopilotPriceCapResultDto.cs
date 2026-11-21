namespace TicketShield.Application.Common.Models.Copilot;

public class CopilotPriceCapResultDto
{
    public string TierName { get; set; } = string.Empty;
    public decimal OriginalPrice { get; set; }
    public decimal ProposedPrice { get; set; }
    public decimal PriceCeiling { get; set; }
    public decimal MaxMarkupPercentage { get; set; }
    public bool IsWithinCeiling { get; set; }
    public string Message { get; set; } = string.Empty;
}
