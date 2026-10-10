using MediatR;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.EscrowBuffer.Models;

namespace TicketShield.Application.Features.Admin.EscrowBuffer.Queries.GetEscrowBuffer;

public class GetEscrowBufferQueryHandler : IRequestHandler<GetEscrowBufferQuery, ApiResponse<EscrowBufferConfig>>
{
    private readonly IEscrowBufferSettings _bufferSettings;

    public GetEscrowBufferQueryHandler(IEscrowBufferSettings bufferSettings)
    {
        _bufferSettings = bufferSettings;
    }

    public async Task<ApiResponse<EscrowBufferConfig>> Handle(GetEscrowBufferQuery request, CancellationToken cancellationToken)
    {
        var config = await _bufferSettings.GetAsync(cancellationToken);
        return ApiResponse<EscrowBufferConfig>.SuccessResponse(config, "Lấy cấu hình thời gian trả tiền thành công.");
    }
}
