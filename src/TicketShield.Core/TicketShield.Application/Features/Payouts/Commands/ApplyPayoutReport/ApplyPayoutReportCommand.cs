using MediatR;

namespace TicketShield.Application.Features.Payouts.Commands.ApplyPayoutReport;

public record ApplyPayoutReportCommand(Guid EscrowId, bool Succeeded, string? BankReference) : IRequest<bool>;
