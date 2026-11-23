namespace MockOrganizer.API.Services;

/// <summary>
/// Trạng thái vi phạm và thời gian phạt Cooldown của người dùng / bot (SCRUM-403).
/// </summary>
public record ViolationStatus
{
    public int Score { get; init; }
    public int CooldownSeconds { get; init; }
    public bool IsHardCutoff { get; init; }
    public long RetryAfterSeconds { get; init; }
}

/// <summary>
/// Giao diện theo dõi bộ đếm vi phạm thống nhất trên Redis (Unified Risk-Weighted Violation Counter).
/// </summary>
public interface IAntiBotViolationTracker
{
    Task<ViolationStatus> GetViolationStatusAsync(
        string? sessionToken,
        string? deviceFingerprint,
        string? clientIp,
        CancellationToken ct = default);

    Task<ViolationStatus> RecordViolationAsync(
        string? sessionToken,
        string? deviceFingerprint,
        string? clientIp,
        int points,
        CancellationToken ct = default);

    Task ResetViolationAsync(
        string? sessionToken,
        string? deviceFingerprint,
        string? clientIp,
        CancellationToken ct = default);
}
