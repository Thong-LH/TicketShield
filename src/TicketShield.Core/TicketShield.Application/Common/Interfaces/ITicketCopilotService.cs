using TicketShield.Application.Common.Models.Copilot;

namespace TicketShield.Application.Common.Interfaces;

public interface ITicketCopilotService
{
    Task<CopilotChatResponse> ChatAsync(CopilotChatRequest request, CancellationToken ct = default);
    IAsyncEnumerable<string> StreamChatAsync(CopilotChatRequest request, CancellationToken ct = default);
    Task<List<CopilotTicketItemDto>> SearchResaleTicketsAsync(string? eventName, string? tierName, decimal? maxPrice, string? zone, string? sortBy = null, int? limit = null, CancellationToken ct = default);
    Task<CopilotPriceCapResultDto> CheckPriceCapAsync(string tierName, decimal proposedPrice, string? eventName = null, CancellationToken ct = default);
}
