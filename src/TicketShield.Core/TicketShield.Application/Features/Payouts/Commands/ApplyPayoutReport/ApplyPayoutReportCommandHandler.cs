using MediatR;
using TicketShield.Application.Common.Interfaces;

namespace TicketShield.Application.Features.Payouts.Commands.ApplyPayoutReport;

public class ApplyPayoutReportCommandHandler : IRequestHandler<ApplyPayoutReportCommand, bool>
{
    private readonly IPayoutReportApplier _applier;

    public ApplyPayoutReportCommandHandler(IPayoutReportApplier applier)
    {
        _applier = applier;
    }

    public Task<bool> Handle(ApplyPayoutReportCommand request, CancellationToken cancellationToken)
        => _applier.ApplyAsync(request.EscrowId, request.Succeeded, request.BankReference, cancellationToken);
}
