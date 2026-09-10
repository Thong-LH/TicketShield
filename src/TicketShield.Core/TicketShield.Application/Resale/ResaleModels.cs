namespace TicketShield.Application.Resale;

public sealed record RequestOtpBody(string TicketCode);
public sealed class ConfirmOtpBody { public string Otp { get; set; } = ""; }
public sealed record PublishBody(string VerificationId, long ResalePrice, bool IsPrivate = false);
public sealed record VerificationResult(string VerificationId, string Status, string? OperationId,
    DateTimeOffset? ExpiresAt, DateTimeOffset? ResendAfter, string? DeliveryState, long? OriginalPrice, Guid? ListingId, string? PrivateAccessToken = null);
public sealed record ListingResult(Guid Id, Guid EventId, Guid TierId, long ResalePrice, string Currency = "VND");
public sealed class ResaleWorkflowException(string code, int httpStatus = 409) : Exception(code)
{
    public string Code { get; } = code;
    public int HttpStatus { get; } = httpStatus;
}
