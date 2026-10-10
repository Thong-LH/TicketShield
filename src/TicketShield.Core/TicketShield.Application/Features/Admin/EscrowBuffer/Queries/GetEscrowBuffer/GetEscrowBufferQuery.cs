using MediatR;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.EscrowBuffer.Models;

namespace TicketShield.Application.Features.Admin.EscrowBuffer.Queries.GetEscrowBuffer;

public record GetEscrowBufferQuery : IRequest<ApiResponse<EscrowBufferConfig>>;
