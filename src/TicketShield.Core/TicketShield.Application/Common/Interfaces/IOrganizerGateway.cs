using TicketShield.Contracts.Organizer.V1;

namespace TicketShield.Application.Common.Interfaces;

/// <summary>
/// Cổng giao tiếp trừu tượng kết nối gRPC với hệ thống Nhà tổ chức (Organizer System)
/// </summary>
public interface IOrganizerGateway
{
    Task<ChallengeView> Request(RequestTicketOtpRequest request, CancellationToken ct);
    Task<ChallengeView> Resend(ResendTicketOtpRequest request, CancellationToken ct);
    Task<VerificationReceipt> Confirm(ConfirmOtpAndLockRequest request, CancellationToken ct);
    Task<OperationView> Operation(GetOperationRequest request, CancellationToken ct);
    Task<VerificationView> Verification(GetVerificationRequest request, CancellationToken ct);
    Task<ResaleLockView> Lock(GetResaleLockRequest request, CancellationToken ct);
    Task<VerificationView> Close(CloseVerificationRequest request, CancellationToken ct);
    Task<ReleaseResaleLockResponse> Release(ReleaseResaleLockRequest request, CancellationToken ct);
}
