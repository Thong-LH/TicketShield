using MediatR;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.EscrowBuffer.Models;

namespace TicketShield.Application.Features.Admin.EscrowBuffer.Commands.UpdateEscrowBuffer;

public class UpdateEscrowBufferCommand : IRequest<ApiResponse<EscrowBufferConfig>>
{
    public int BufferSeconds { get; set; }
    public int CutoffSeconds { get; set; }
}
