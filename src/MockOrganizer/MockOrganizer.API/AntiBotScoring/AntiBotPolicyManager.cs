using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace MockOrganizer.API.AntiBotScoring;

/// <summary>
/// Quản lý chính sách ngưỡng động cho phân hệ Anti-Bot (US-8.4, SCRUM-260).
/// Thread-safe Singleton hỗ trợ cập nhật cấu hình tức thì không cần restart tiến trình.
/// </summary>
public class AntiBotPolicyManager : IAntiBotPolicyManager
{
    private readonly ConcurrentDictionary<string, AntiBotPolicyConfig> _policies = new();
    private readonly AntiBotPolicyConfig _defaultPolicy;
    private readonly ILogger<AntiBotPolicyManager> _logger;

    public AntiBotPolicyManager(
        IOptions<AntiBotScoringOptions>? options,
        ILogger<AntiBotPolicyManager> logger)
    {
        _logger = logger;

        float defaultAllow = options?.Value.AllowThreshold ?? 0.35f;
        float defaultBlock = options?.Value.BlockThreshold ?? 0.75f;

        _defaultPolicy = new AntiBotPolicyConfig
        {
            AllowThreshold = defaultAllow,
            BlockThreshold = defaultBlock,
            ChallengeMode = "recaptcha",
            LastUpdatedAtUtc = DateTime.UtcNow,
            UpdatedBy = "InitialConfig"
        };

        _policies[string.Empty] = _defaultPolicy;
    }

    public AntiBotPolicyConfig GetPolicy(string? eventId = null)
    {
        if (!string.IsNullOrWhiteSpace(eventId) && _policies.TryGetValue(eventId, out var eventPolicy))
        {
            return eventPolicy;
        }

        return _policies.TryGetValue(string.Empty, out var globalPolicy) ? globalPolicy : _defaultPolicy;
    }

    public bool TryUpdatePolicy(
        float allowThreshold,
        float blockThreshold,
        out string? errorMessage,
        string? eventId = null,
        string? updatedBy = null,
        string? challengeMode = null)
    {
        // 1. Kiểm tra miền giá trị hợp lệ
        if (allowThreshold < 0.01f || allowThreshold > 0.95f)
        {
            errorMessage = "Ngưỡng Cho phép (Allow) phải nằm trong khoảng từ 0.01 đến 0.95.";
            return false;
        }

        if (blockThreshold < 0.05f || blockThreshold > 0.99f)
        {
            errorMessage = "Ngưỡng Chặn (Block) phải nằm trong khoảng từ 0.05 đến 0.99.";
            return false;
        }

        if (allowThreshold >= blockThreshold)
        {
            errorMessage = $"Ngưỡng Cho phép ({allowThreshold:F2}) phải nhỏ hơn Ngưỡng Chặn ({blockThreshold:F2}).";
            return false;
        }

        string normalizedMode = challengeMode?.Trim().ToLowerInvariant() switch
        {
            "turnstile" => "turnstile",
            "sequential" => "sequential",
            _ => "recaptcha"
        };

        // 2. Cập nhật chính sách
        string key = string.IsNullOrWhiteSpace(eventId) ? string.Empty : eventId.Trim();
        var newPolicy = new AntiBotPolicyConfig
        {
            AllowThreshold = (float)Math.Round(allowThreshold, 4),
            BlockThreshold = (float)Math.Round(blockThreshold, 4),
            ChallengeMode = normalizedMode,
            LastUpdatedAtUtc = DateTime.UtcNow,
            UpdatedBy = string.IsNullOrWhiteSpace(updatedBy) ? "Admin" : updatedBy.Trim()
        };

        _policies[key] = newPolicy;

        _logger.LogInformation(
            "AntiBot Policy Updated successfully [{Scope}]: AllowThreshold={Allow}, BlockThreshold={Block}, UpdatedBy={User}",
            string.IsNullOrEmpty(key) ? "Global" : $"Event: {key}",
            newPolicy.AllowThreshold, newPolicy.BlockThreshold, newPolicy.UpdatedBy);

        errorMessage = null;
        return true;
    }

    public void ResetToDefault(string? eventId = null)
    {
        string key = string.IsNullOrWhiteSpace(eventId) ? string.Empty : eventId.Trim();
        if (string.IsNullOrEmpty(key))
        {
            _policies[string.Empty] = _defaultPolicy;
        }
        else
        {
            _policies.TryRemove(key, out _);
        }

        _logger.LogInformation("AntiBot Policy reset to default [{Scope}]",
            string.IsNullOrEmpty(key) ? "Global" : $"Event: {key}");
    }
}
