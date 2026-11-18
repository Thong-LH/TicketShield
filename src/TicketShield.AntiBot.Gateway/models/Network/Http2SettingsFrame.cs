using System.Text.Json.Serialization;

namespace TicketShield.AntiBot.Gateway.Models.Network;

/// <summary>
/// Đại diện cho các tham số trong frame HTTP/2 SETTINGS theo chuẩn RFC 7540 / Akamai Fingerprint:
/// Định dạng: 1:value;2:value;4:value;6:value
/// </summary>
public record Http2SettingsFrame
{
    /// <summary>
    /// ID 0x1: Kích thước bảng mã hóa tiêu đề (SETTINGS_HEADER_TABLE_SIZE).
    /// Chrome mặc định: 65536.
    /// </summary>
    [JsonPropertyName("headerTableSize")]
    public uint? HeaderTableSize { get; init; }

    /// <summary>
    /// ID 0x2: Cho phép Server Push (SETTINGS_ENABLE_PUSH).
    /// Hầu hết trình duyệt hiện đại: 0.
    /// </summary>
    [JsonPropertyName("enablePush")]
    public uint? EnablePush { get; init; }

    /// <summary>
    /// ID 0x3: Số stream đồng thời tối đa (SETTINGS_MAX_CONCURRENT_STREAMS).
    /// Chrome: 1000 (nếu khai báo).
    /// </summary>
    [JsonPropertyName("maxConcurrentStreams")]
    public uint? MaxConcurrentStreams { get; init; }

    /// <summary>
    /// ID 0x4: Kích thước cửa sổ luồng ban đầu (SETTINGS_INITIAL_WINDOW_SIZE).
    /// Chrome mặc định: 6291456 (6MB).
    /// </summary>
    [JsonPropertyName("initialWindowSize")]
    public uint? InitialWindowSize { get; init; }

    /// <summary>
    /// ID 0x5: Kích thước frame tối đa (SETTINGS_MAX_FRAME_SIZE).
    /// Mặc định RFC: 16384.
    /// </summary>
    [JsonPropertyName("maxFrameSize")]
    public uint? MaxFrameSize { get; init; }

    /// <summary>
    /// ID 0x6: Kích thước tối đa của danh sách tiêu đề (SETTINGS_MAX_HEADER_LIST_SIZE).
    /// Chrome mặc định: 262144 (256KB).
    /// </summary>
    [JsonPropertyName("maxHeaderListSize")]
    public uint? MaxHeaderListSize { get; init; }

    /// <summary>
    /// Chuỗi SETTINGS thô theo chuẩn Akamai (ví dụ: "1:65536;2:0;4:6291456;6:262144").
    /// </summary>
    [JsonPropertyName("rawSettings")]
    public string RawSettings { get; init; } = string.Empty;

    /// <summary>
    /// Cờ xác định chuỗi SETTINGS có hợp lệ cú pháp hay không.
    /// </summary>
    [JsonPropertyName("isValid")]
    public bool IsValid { get; init; }

    /// <summary>
    /// Đối tượng rỗng đại diện cho trường hợp không có hoặc phân tích thất bại.
    /// </summary>
    public static readonly Http2SettingsFrame Empty = new()
    {
        RawSettings = string.Empty,
        IsValid = false
    };
}
