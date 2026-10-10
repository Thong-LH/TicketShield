using Microsoft.Extensions.Caching.Memory;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.Admin.EscrowBuffer.Models;

namespace TicketShield.Infrastructure.Services;

public class EscrowBufferSettings : IEscrowBufferSettings
{
    public const string CacheKey = "CACHE_ESCROW_BUFFER_CONFIG";

    private readonly ISystemSettingRepository _settingRepository;
    private readonly IMemoryCache _cache;

    public EscrowBufferSettings(ISystemSettingRepository settingRepository, IMemoryCache cache)
    {
        _settingRepository = settingRepository;
        _cache = cache;
    }

    public async Task<EscrowBufferConfig> GetAsync(CancellationToken ct = default)
    {
        var config = await _cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            return await _settingRepository.GetEscrowBufferConfigAsync(ct);
        });

        return config ?? new EscrowBufferConfig();
    }

    public async Task UpdateAsync(EscrowBufferConfig config, Guid? updatedBy, CancellationToken ct = default)
    {
        await _settingRepository.UpdateEscrowBufferConfigAsync(config, updatedBy, ct);
        InvalidateCache();
    }

    public void InvalidateCache()
    {
        _cache.Remove(CacheKey);
    }
}
