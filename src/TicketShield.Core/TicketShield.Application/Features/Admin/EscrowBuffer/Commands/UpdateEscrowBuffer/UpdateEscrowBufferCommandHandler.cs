using MediatR;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.EscrowBuffer.Models;

namespace TicketShield.Application.Features.Admin.EscrowBuffer.Commands.UpdateEscrowBuffer;

public class UpdateEscrowBufferCommandHandler : IRequestHandler<UpdateEscrowBufferCommand, ApiResponse<EscrowBufferConfig>>
{
    private readonly IEscrowBufferSettings _bufferSettings;
    private readonly ICurrentUserService _currentUserService;

    public UpdateEscrowBufferCommandHandler(
        IEscrowBufferSettings bufferSettings,
        ICurrentUserService currentUserService)
    {
        _bufferSettings = bufferSettings;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<EscrowBufferConfig>> Handle(UpdateEscrowBufferCommand request, CancellationToken cancellationToken)
    {
        var config = new EscrowBufferConfig
        {
            BufferSeconds = request.BufferSeconds,
            CutoffSeconds = request.CutoffSeconds
        };

        await _bufferSettings.UpdateAsync(config, _currentUserService.UserId, cancellationToken);
        return ApiResponse<EscrowBufferConfig>.SuccessResponse(config, "Cập nhật thời gian trả tiền thành công.");
    }
}
