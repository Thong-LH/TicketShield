using System.Net;
using System.Text.RegularExpressions;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Logging;
using TicketShield.Application.Common.Interfaces;

namespace TicketShield.Infrastructure.Services;

/// <summary>
/// Cất ảnh bằng chứng khiếu nại trên Cloudinary ở chế độ authenticated.
/// Key trả về (và lưu vào dispute_evidences.file_url) là secure_url có chữ ký; link này chỉ BE dùng.
/// </summary>
public class CloudinaryEvidenceFileStore : IDisputeEvidenceFileStore
{
    public const string HttpClientName = "cloudinary-evidence";

    private const string Folder = "dispute-evidence";
    private const string DeliveryType = "authenticated";

    private readonly CloudinaryOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CloudinaryEvidenceFileStore> _logger;
    private readonly Regex _secureUrl;

    public CloudinaryEvidenceFileStore(
        CloudinaryOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<CloudinaryEvidenceFileStore> logger)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _secureUrl = new Regex(
            $@"^https://res\.cloudinary\.com/{Regex.Escape(options.CloudName)}/image/{DeliveryType}/s--[A-Za-z0-9_-]+--/(?:v\d+/)?(?<publicId>{Folder}/[0-9a-f]{{32}})\.(?:jpg|png|webp)$",
            RegexOptions.CultureInvariant);
    }

    public async Task<string> SaveAsync(byte[] content, string extension, CancellationToken cancellationToken = default)
    {
        var name = Guid.NewGuid().ToString("N");
        var publicId = $"{Folder}/{name}";
        using var stream = new MemoryStream(content);
        var result = await CreateCloudinary().UploadAsync(new ImageUploadParams
        {
            File = new FileDescription($"{name}.{extension}", stream),
            PublicId = publicId,
            Type = DeliveryType,
            AssetFolder = Folder,
            Overwrite = false
        }, cancellationToken);

        if (result.Error != null || result.SecureUrl == null)
        {
            throw new InvalidOperationException($"Cloudinary evidence upload rejected: {result.Error?.Message}");
        }

        // Link không khớp mẫu thì ReadAsync sẽ từ chối về sau, nên gỡ ảnh ngay thay vì lưu một link chết.
        var secureUrl = result.SecureUrl.AbsoluteUri;
        var match = _secureUrl.Match(secureUrl);
        if (!match.Success || match.Groups["publicId"].Value != publicId)
        {
            await DestroyAsync(publicId);
            throw new InvalidOperationException("Cloudinary returned an invalid evidence image URL format.");
        }

        return secureUrl;
    }

    public async Task<byte[]?> ReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        if (!_secureUrl.IsMatch(storageKey))
        {
            return null;
        }

        using var http = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await http.GetAsync(storageKey, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Cloudinary returned status code {(int)response.StatusCode} when downloading evidence image.");
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var match = _secureUrl.Match(storageKey);
        if (match.Success)
        {
            await DestroyAsync(match.Groups["publicId"].Value);
        }
    }

    // Chỉ chạy ở nhánh dọn dẹp, nên lỗi ở đây ghi log rồi bỏ qua để lỗi gốc vẫn được ném ra.
    private async Task DestroyAsync(string publicId)
    {
        try
        {
            var result = await CreateCloudinary().DestroyAsync(new DeletionParams(publicId)
            {
                Type = DeliveryType,
                ResourceType = ResourceType.Image,
                Invalidate = true
            });
            if (result.Error != null || (result.Result != "ok" && result.Result != "not found"))
            {
                _logger.LogWarning("Failed to delete evidence image {PublicId} on Cloudinary: {Reason}",
                    publicId, result.Error?.Message ?? result.Result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete evidence image {PublicId} on Cloudinary", publicId);
        }
    }

    private Cloudinary CreateCloudinary()
    {
        var cloudinary = new Cloudinary(new Account(_options.CloudName, _options.ApiKey, _options.ApiSecret));
        cloudinary.Api.Secure = true;
        // Gọi SDK qua client của IHttpClientFactory; upload ảnh 5 MB cần lâu hơn mức 10 giây dành cho tải ảnh.
        var http = _httpClientFactory.CreateClient(HttpClientName);
        http.Timeout = TimeSpan.FromSeconds(60);
        cloudinary.Api.Client = http;
        return cloudinary;
    }
}
