using MediatR;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.FeeSettings.Models;

namespace TicketShield.Application.Features.Admin.FeeSettings.Queries.GetFeeSettings;

public class GetFeeSettingsQueryHandler : IRequestHandler<GetFeeSettingsQuery, ApiResponse<ResaleFeeConfig>>
{
    private readonly ISystemSettingRepository _settingRepository;

    public GetFeeSettingsQueryHandler(ISystemSettingRepository settingRepository)
    {
        _settingRepository = settingRepository;
    }

    public async Task<ApiResponse<ResaleFeeConfig>> Handle(GetFeeSettingsQuery request, CancellationToken cancellationToken)
    {
        var config = await _settingRepository.GetResaleFeeConfigAsync(cancellationToken);
        return ApiResponse<ResaleFeeConfig>.SuccessResponse(config, "Lấy cấu hình biểu phí hệ thống thành công.");
    }
}
