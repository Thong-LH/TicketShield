using System.Net;
using System.Text.RegularExpressions;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Logging;
using TicketShield.Application.Common.Interfaces;

namespace TicketShield.Infrastructure.Services;

/// <summary>
/// Triển khai dịch vụ lưu trữ Cloudinary phục vụ ảnh sự kiện, banner và tài sản số.
/// Kế thừa mô hình Clean Architecture với cơ chế tự phục hồi 2 tầng (Two-Tier Resilience with Fallback).
/// </summary>
public class CloudinaryStorageService : IStorageService
{
    public const string HttpClientName = "cloudinary-storage";
    private const int MaxDownloadRetries = 4;
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(1.5);

    private readonly CloudinaryOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CloudinaryStorageService> _logger;

    public CloudinaryStorageService(
        CloudinaryOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<CloudinaryStorageService> logger)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> UploadAsync(
        string sourceUrl,
        string fileName,
        string? subFolder = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            throw new ArgumentException("Source URL cannot be null or empty.", nameof(sourceUrl));
        }

        // Nếu môi trường test hoặc local chưa nạp key Cloudinary, fallback an toàn về sourceUrl
        if (string.IsNullOrWhiteSpace(_options.CloudName) ||
            string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.ApiSecret))
        {
            _logger.LogWarning("[Cloudinary] Khóa API chưa được cấu hình, fallback về URL nguồn: {SourceUrl}", sourceUrl);
            return sourceUrl;
        }

        var sanitizedFileName = SanitizePublicId(fileName);
        var folder = string.IsNullOrWhiteSpace(subFolder)
            ? _options.RootFolder
            : $"{_options.RootFolder.TrimEnd('/')}/{subFolder.Trim('/')}";
        var publicId = $"{folder}/{sanitizedFileName}";

        var cloudinary = CreateCloudinary();

        // 1. Trường hợp Data URL Base64
        if (sourceUrl.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
        {
            return await UploadBase64Async(cloudinary, sourceUrl, sanitizedFileName, publicId, folder, cancellationToken);
        }

        // 2. Trường hợp Remote URL: Thử đẩy thẳng qua Cloudinary Pull
        try
        {
            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(sourceUrl),
                PublicId = publicId,
                Folder = folder,
                Overwrite = true,
                Type = "upload"
            };

            var result = await cloudinary.UploadAsync(uploadParams, cancellationToken);
            if (result.Error == null && result.SecureUrl != null)
            {
                _logger.LogInformation("[Cloudinary] Upload thành công qua Remote URL: {SecureUrl}", result.SecureUrl.AbsoluteUri);
                return result.SecureUrl.AbsoluteUri;
            }

            _logger.LogWarning("[Cloudinary] Upload trực tiếp thất bại ({Error}), kích hoạt tầng Fallback tự tải stream...", result.Error?.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Cloudinary] Ngoại lệ khi upload trực tiếp từ URL nguồn {SourceUrl}, kích hoạt tầng Fallback...", sourceUrl);
        }

        // 3. Cơ chế phục hồi 2 tầng (Two-Tier Resilience): Tự tải Stream về RAM với chính sách Retry 4 lần
        var downloadedBytes = await DownloadSourceAsync(sourceUrl, cancellationToken);
        if (downloadedBytes != null && downloadedBytes.Length > 0)
        {
            using var stream = new MemoryStream(downloadedBytes);
            var streamParams = new ImageUploadParams
            {
                File = new FileDescription($"{sanitizedFileName}.jpg", stream),
                PublicId = publicId,
                Folder = folder,
                Overwrite = true,
                Type = "upload"
            };

            var streamResult = await cloudinary.UploadAsync(streamParams, cancellationToken);
            if (streamResult.Error == null && streamResult.SecureUrl != null)
            {
                _logger.LogInformation("[Cloudinary] Upload thành công sau khi phục hồi stream: {SecureUrl}", streamResult.SecureUrl.AbsoluteUri);
                return streamResult.SecureUrl.AbsoluteUri;
            }

            _logger.LogError("[Cloudinary] Upload stream thất bại: {Error}", streamResult.Error?.Message);
        }

        // 4. Nếu toàn bộ nỗ lực CDN thất bại, bảo toàn luồng bằng sourceUrl gốc thay vì làm crash hệ thống
        _logger.LogWarning("[Cloudinary] Không thể đẩy ảnh lên CDN, fallback an toàn về URL nguồn ban đầu: {SourceUrl}", sourceUrl);
        return sourceUrl;
    }

    private async Task<string> UploadBase64Async(
        Cloudinary cloudinary,
        string base64Data,
        string fileName,
        string publicId,
        string folder,
        CancellationToken cancellationToken)
    {
        var commaIndex = base64Data.IndexOf(',');
        var cleanBase64 = commaIndex >= 0 ? base64Data[(commaIndex + 1)..] : base64Data;
        var bytes = Convert.FromBase64String(cleanBase64);

        using var stream = new MemoryStream(bytes);
        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription($"{fileName}.jpg", stream),
            PublicId = publicId,
            Folder = folder,
            Overwrite = true,
            Type = "upload"
        };

        var result = await cloudinary.UploadAsync(uploadParams, cancellationToken);
        if (result.Error != null || result.SecureUrl == null)
        {
            throw new InvalidOperationException($"Cloudinary base64 upload failed: {result.Error?.Message}");
        }

        return result.SecureUrl.AbsoluteUri;
    }

    private async Task<byte[]?> DownloadSourceAsync(string url, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxDownloadRetries; attempt++)
        {
            try
            {
                _logger.LogInformation("[Cloudinary Fallback] Tải stream nguồn (Lần thử {Attempt}/{Max}): {Url}", attempt, MaxDownloadRetries, url);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(25));

                using var httpClient = _httpClientFactory.CreateClient(HttpClientName);
                using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return await response.Content.ReadAsByteArrayAsync(cts.Token);
                }

                _logger.LogWarning("[Cloudinary Fallback] Tải nguồn thất bại với HTTP status {Status}", response.StatusCode);
            }
            catch (Exception ex) when (attempt < MaxDownloadRetries)
            {
                _logger.LogWarning(ex, "[Cloudinary Fallback] Lỗi kết nối ở lần thử {Attempt}, chờ {Delay}s để thử lại...", attempt, RetryInterval.TotalSeconds);
                await Task.Delay(RetryInterval, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Cloudinary Fallback] Đã cạn số lần thử tải ảnh nguồn từ {Url}", url);
            }
        }

        return null;
    }

    private static string SanitizePublicId(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Guid.NewGuid().ToString("N");
        }

        // Lọc bỏ ký tự đặc biệt, chỉ giữ ký tự chữ, số, gạch ngang, gạch dưới
        var sanitized = Regex.Replace(input, @"[^a-zA-Z0-9_\-]", "_");
        return sanitized.Trim('_').ToLowerInvariant();
    }

    private Cloudinary CreateCloudinary()
    {
        var account = new Account(_options.CloudName, _options.ApiKey, _options.ApiSecret);
        var cloudinary = new Cloudinary(account)
        {
            Api = { Secure = true }
        };

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        httpClient.Timeout = TimeSpan.FromSeconds(60);
        cloudinary.Api.Client = httpClient;

        return cloudinary;
    }
}
