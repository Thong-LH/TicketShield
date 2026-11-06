using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.Admin.FeeSettings.Models;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Infrastructure.Services;

public class DynamicResaleFeeCalculator : IResaleFeeCalculator
{
    private readonly ISystemSettingRepository _settingRepository;
    private readonly IMemoryCache _cache;
    private readonly ITicketShieldDbContext? _dbContext;
    public const string FeeConfigCacheKey = "CACHE_RESALE_FEE_CONFIG";

    public DynamicResaleFeeCalculator(
        ISystemSettingRepository settingRepository,
        IMemoryCache cache,
        ITicketShieldDbContext? dbContext = null)
    {
        _settingRepository = settingRepository;
        _cache = cache;
        _dbContext = dbContext;
    }

    public Task<ResaleFeeCalculationResult> CalculateFeeAsync(decimal resalePrice, bool isPrivate, CancellationToken ct = default)
        => CalculateFeeAsync(resalePrice, isPrivate, null, ct);

    public async Task<ResaleFeeCalculationResult> CalculateFeeAsync(decimal resalePrice, bool isPrivate, Guid? eventId, CancellationToken ct = default)
    {
        Commission? commission = null;
        if (eventId.HasValue && _dbContext != null)
        {
            var cacheKey = $"CACHE_COMMISSION_EVENT_{eventId.Value}";
            commission = await _cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                var now = DateTimeOffset.UtcNow;
                return await _dbContext.Commissions
                    .AsNoTracking()
                    .Where(c => c.EventId == eventId.Value && c.IsActive && c.EffectiveFrom <= now && (!c.EffectiveTo.HasValue || c.EffectiveTo.Value >= now))
                    .OrderByDescending(c => c.CreatedAt)
                    .FirstOrDefaultAsync(ct);
            });
        }

        decimal buyerFeePercentage;
        decimal sellerFeePercentage;
        decimal minimumBuyerFee;
        decimal minimumSellerFee;

        if (commission != null)
        {
            buyerFeePercentage = commission.BuyerFeeRate;
            sellerFeePercentage = commission.SellerFeeRate;
            minimumBuyerFee = commission.MinimumBuyerFee;
            minimumSellerFee = commission.MinimumSellerFee;
        }
        else
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

            buyerFeePercentage = feeConfig.BuyerFeePercentage;
            sellerFeePercentage = feeConfig.SellerFeePercentage;
            minimumBuyerFee = feeConfig.MinimumBuyerFee;
            minimumSellerFee = feeConfig.MinimumSellerFee;
        }

        if (resalePrice < minimumSellerFee)
        {
            throw new BusinessRuleViolationException(
                $"Giá bán lại ({resalePrice:N0} VNĐ) phải lớn hơn hoặc bằng phí sàn tối thiểu ({minimumSellerFee:N0} VNĐ).");
        }

        // UNIFORM FEE CALCULATION FORMULA (Applies identically for both Public & Private Resale)
        decimal rawBuyerFee = Math.Max(resalePrice * buyerFeePercentage, minimumBuyerFee);
        decimal rawSellerFee = Math.Max(resalePrice * sellerFeePercentage, minimumSellerFee);

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
