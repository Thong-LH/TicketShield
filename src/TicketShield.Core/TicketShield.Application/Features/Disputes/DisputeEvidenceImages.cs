namespace TicketShield.Application.Features.Disputes;

public static class DisputeEvidenceImages
{
    public const int MaxBytes = 5 * 1024 * 1024;

    public static bool TryGetFormat(byte[]? content, string? contentType, out string extension, out string mediaType)
    {
        extension = string.Empty;
        mediaType = string.Empty;
        if (content == null || content.Length == 0 || content.Length > MaxBytes)
        {
            return false;
        }

        var declared = contentType?.Split(';')[0].Trim().ToLowerInvariant();
        if (declared == "image/jpg")
        {
            declared = "image/jpeg";
        }

        if (declared == "image/jpeg" && IsJpeg(content))
        {
            extension = "jpg";
            mediaType = "image/jpeg";
            return true;
        }

        if (declared == "image/png" && IsPng(content))
        {
            extension = "png";
            mediaType = "image/png";
            return true;
        }

        if (declared == "image/webp" && IsWebp(content))
        {
            extension = "webp";
            mediaType = "image/webp";
            return true;
        }

        return false;
    }

    public static string MediaTypeFor(string storageKey)
    {
        var extension = Path.GetExtension(storageKey).TrimStart('.').ToLowerInvariant();
        return extension switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            "webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }

    private static bool IsJpeg(byte[] content)
        => content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF;

    private static bool IsPng(byte[] content)
        => content.Length >= 8
           && content[0] == 0x89
           && content[1] == 0x50
           && content[2] == 0x4E
           && content[3] == 0x47
           && content[4] == 0x0D
           && content[5] == 0x0A
           && content[6] == 0x1A
           && content[7] == 0x0A;

    private static bool IsWebp(byte[] content)
        => content.Length >= 12
           && content[0] == (byte)'R'
           && content[1] == (byte)'I'
           && content[2] == (byte)'F'
           && content[3] == (byte)'F'
           && content[8] == (byte)'W'
           && content[9] == (byte)'E'
           && content[10] == (byte)'B'
           && content[11] == (byte)'P';
}
