using MediatR;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.FeeSettings.Models;

namespace TicketShield.Application.Features.Admin.FeeSettings.Commands.UpdateFeeSettings;

public class UpdateFeeSettingsCommandHandler : IRequestHandler<UpdateFeeSettingsCommand, ApiResponse<string>>
{
    private readonly ISystemSettingRepository _settingRepository;
    private readonly IResaleFeeCalculator _feeCalculator;
    private readonly ICurrentUserService _currentUserService;

    public UpdateFeeSettingsCommandHandler(
        ISystemSettingRepository settingRepository,
        IResaleFeeCalculator feeCalculator,
        ICurrentUserService currentUserService)
    {
        _settingRepository = settingRepository;
        _feeCalculator = feeCalculator;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<string>> Handle(UpdateFeeSettingsCommand request, CancellationToken cancellationToken)
    {
        Guid? currentUserId = _currentUserService.UserId;

        var config = new ResaleFeeConfig
        {
            BuyerFeePercentage = request.BuyerFeePercentage,
            SellerFeePercentage = request.SellerFeePercentage,
            MinimumBuyerFee = request.MinimumBuyerFee,
            MinimumSellerFee = request.MinimumSellerFee
        };

        // 1. Update Database Persistence
        await _settingRepository.UpdateResaleFeeConfigAsync(config, currentUserId, cancellationToken);

        // 2. Invalidate Cache Key CACHE_RESALE_FEE_CONFIG
        _feeCalculator.InvalidateCache();

        return ApiResponse<string>.SuccessResponse("Cập nhật biểu phí hệ thống và xóa cache thành công.", "Cập nhật biểu phí thành công.");
    }
}
