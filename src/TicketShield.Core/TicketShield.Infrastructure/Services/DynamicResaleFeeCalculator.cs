using Microsoft.Extensions.Caching.Memory;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.Admin.FeeSettings.Models;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Infrastructure.Services;

public class DynamicResaleFeeCalculator : IResaleFeeCalculator
{
    private readonly ISystemSettingRepository _settingRepository;
    private readonly IMemoryCache _cache;
    public const string FeeConfigCacheKey = "CACHE_RESALE_FEE_CONFIG";

    public DynamicResaleFeeCalculator(ISystemSettingRepository settingRepository, IMemoryCache cache)
    {
        _settingRepository = settingRepository;
        _cache = cache;
    }

    public async Task<ResaleFeeCalculationResult> CalculateFeeAsync(decimal resalePrice, bool isPrivate, CancellationToken ct = default)
    {
        var feeConfig = await _cache.GetOrCreateAsync(FeeConfigCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            return await _settingRepository.GetResaleFeeConfigAsync(ct);
        });

        if (feeConfig == null)
        {
            feeConfig = new ResaleFeeConfig();
        }

        if (resalePrice < feeConfig.MinimumSellerFee)
        {
            throw new BusinessRuleViolationException(
                $"Giá bán lại ({resalePrice:N0} VNĐ) phải lớn hơn hoặc bằng phí sàn tối thiểu ({feeConfig.MinimumSellerFee:N0} VNĐ).");
        }

        // UNIFORM FEE CALCULATION FORMULA (Applies identically for both Public & Private Resale)
        decimal rawBuyerFee = Math.Max(resalePrice * feeConfig.BuyerFeePercentage, feeConfig.MinimumBuyerFee);
        decimal rawSellerFee = Math.Max(resalePrice * feeConfig.SellerFeePercentage, feeConfig.MinimumSellerFee);

        decimal buyerFee = Math.Round(rawBuyerFee, 0);
        decimal sellerFee = Math.Round(rawSellerFee, 0);

        return new ResaleFeeCalculationResult
        {
            OriginalPrice = resalePrice,
            BuyerFee = buyerFee,
            SellerFee = sellerFee,
            TotalBuyerPaid = Math.Round(resalePrice + buyerFee, 0),
            NetSellerPayout = Math.Round(resalePrice - sellerFee, 0)
        };
    }

    public void InvalidateCache()
    {
        _cache.Remove(FeeConfigCacheKey);
    }
}
