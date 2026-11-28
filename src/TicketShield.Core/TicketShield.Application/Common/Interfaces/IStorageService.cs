namespace TicketShield.Application.Common.Interfaces;

/// <summary>
/// Dịch vụ lưu trữ và phân phối tài sản truyền thông (Media/Assets) trên Cloud CDN.
/// Decoupling hoàn toàn nghiệp vụ ứng dụng khỏi nhà cung cấp lưu trữ ngoại vi (Cloudinary).
/// </summary>
public interface IStorageService
{
    /// <summary>
    /// Đẩy ảnh/video lên Cloudinary từ Remote URL hoặc Base64 string và trả về CDN HTTPS SecureUrl.
    /// </summary>
    /// <param name="sourceUrl">Đường dẫn ảnh nguồn (Remote URL hoặc Data URL base64).</param>
    /// <param name="fileName">Tên định danh file (sẽ được tự động làm sạch ký tự cấm).</param>
    /// <param name="subFolder">Thư mục con phân cấp (ví dụ: events/atsh-2026).</param>
    /// <param name="cancellationToken">Token hủy tác vụ.</param>
    /// <returns>Chuỗi CDN HTTPS SecureUrl dùng để lưu vào Database Entity.</returns>
    Task<string> UploadAsync(string sourceUrl, string fileName, string? subFolder = null, CancellationToken cancellationToken = default);
}
