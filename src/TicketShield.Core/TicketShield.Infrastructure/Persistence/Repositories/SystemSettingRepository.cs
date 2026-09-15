using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.Admin.FeeSettings.Models;
using TicketShield.Domain.Entities;

namespace TicketShield.Infrastructure.Persistence.Repositories;

public class SystemSettingRepository : ISystemSettingRepository
{
    private readonly ITicketShieldDbContext _context;

    public const string BuyerPercentageKey = "ResaleFee_BuyerPercentage";
    public const string SellerPercentageKey = "ResaleFee_SellerPercentage";
    public const string MinBuyerFeeKey = "ResaleFee_MinBuyerFee";
    public const string MinSellerFeeKey = "ResaleFee_MinSellerFee";

    public SystemSettingRepository(ITicketShieldDbContext context)
    {
        _context = context;
    }

    public async Task<ResaleFeeConfig> GetResaleFeeConfigAsync(CancellationToken ct = default)
    {
        var settings = await _context.SystemSettings
            .Where(s => s.SettingKey == BuyerPercentageKey ||
                        s.SettingKey == SellerPercentageKey ||
                        s.SettingKey == MinBuyerFeeKey ||
                        s.SettingKey == MinSellerFeeKey)
            .ToDictionaryAsync(s => s.SettingKey, s => s.SettingValue, ct);

        var config = new ResaleFeeConfig();

        if (settings.TryGetValue(BuyerPercentageKey, out var buyerPctStr) &&
            decimal.TryParse(buyerPctStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var buyerPct))
        {
            config.BuyerFeePercentage = buyerPct;
        }

        if (settings.TryGetValue(SellerPercentageKey, out var sellerPctStr) &&
            decimal.TryParse(sellerPctStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var sellerPct))
        {
            config.SellerFeePercentage = sellerPct;
        }

        if (settings.TryGetValue(MinBuyerFeeKey, out var minBuyerStr) &&
            decimal.TryParse(minBuyerStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var minBuyer))
        {
            config.MinimumBuyerFee = minBuyer;
        }

        if (settings.TryGetValue(MinSellerFeeKey, out var minSellerStr) &&
            decimal.TryParse(minSellerStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var minSeller))
        {
            config.MinimumSellerFee = minSeller;
        }

        return config;
    }

    public async Task UpdateResaleFeeConfigAsync(ResaleFeeConfig config, Guid? updatedBy, CancellationToken ct = default)
    {
        var items = new (string Key, string Value, string DataType, string Description)[]
        {
            (BuyerPercentageKey, config.BuyerFeePercentage.ToString(CultureInfo.InvariantCulture), "Decimal", "Tỷ lệ phí người mua"),
            (SellerPercentageKey, config.SellerFeePercentage.ToString(CultureInfo.InvariantCulture), "Decimal", "Tỷ lệ phí người bán"),
            (MinBuyerFeeKey, config.MinimumBuyerFee.ToString(CultureInfo.InvariantCulture), "Money", "Phí tối thiểu người mua"),
            (MinSellerFeeKey, config.MinimumSellerFee.ToString(CultureInfo.InvariantCulture), "Money", "Phí tối thiểu người bán")
        };

        foreach (var item in items)
        {
            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.SettingKey == item.Key, ct);

            if (setting == null)
            {
                setting = new SystemSetting
                {
                    SettingKey = item.Key,
                    SettingValue = item.Value,
                    DataType = item.DataType,
                    Description = item.Description,
                    UpdatedBy = updatedBy,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await _context.SystemSettings.AddAsync(setting, ct);
            }
            else
            {
                setting.SettingValue = item.Value;
                setting.UpdatedBy = updatedBy;
                setting.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await _context.SaveChangesAsync(ct);
    }
}
