using Grpc.Core;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Contracts.Organizer.V1;

namespace TicketShield.Infrastructure.ExternalServices.Organizer;

/// <summary>
/// Client gRPC giao tiếp thực tế với hệ thống Nhà tổ chức (Organizer System)
/// </summary>
public sealed class OrganizerGateway : IOrganizerGateway
{
    private readonly OrganizerResaleService.OrganizerResaleServiceClient _client;
    private readonly OrganizerConnectionOptions _options;

    public OrganizerGateway(
        OrganizerResaleService.OrganizerResaleServiceClient client,
        OrganizerConnectionOptions options)
    {
        _client = client;
        _options = options;
    }

    private CallOptions CreateCallOptions(CancellationToken ct)
    {
        var metadata = new Metadata
        {
            { "authorization", "Bearer " + _options.ApiKey }
        };
        var deadline = DateTime.UtcNow.AddSeconds(_options.DeadlineSeconds);
        return new CallOptions(metadata, deadline, ct);
    }

    public async Task<ChallengeView> Request(RequestTicketOtpRequest request, CancellationToken ct)
    {
        using var call = _client.RequestTicketOtpAsync(request, CreateCallOptions(ct));
        return await call.ResponseAsync;
    }

    public async Task<ChallengeView> Resend(ResendTicketOtpRequest request, CancellationToken ct)
    {
        using var call = _client.ResendTicketOtpAsync(request, CreateCallOptions(ct));
        return await call.ResponseAsync;
    }

    public async Task<VerificationReceipt> Confirm(ConfirmOtpAndLockRequest request, CancellationToken ct)
    {
        using var call = _client.ConfirmOtpAndLockAsync(request, CreateCallOptions(ct));
        return await call.ResponseAsync;
    }

    public async Task<OperationView> Operation(GetOperationRequest request, CancellationToken ct)
    {
        using var call = _client.GetOperationAsync(request, CreateCallOptions(ct));
        return await call.ResponseAsync;
    }

    public async Task<VerificationView> Verification(GetVerificationRequest request, CancellationToken ct)
    {
        using var call = _client.GetVerificationAsync(request, CreateCallOptions(ct));
        return await call.ResponseAsync;
    }

    public async Task<ResaleLockView> Lock(GetResaleLockRequest request, CancellationToken ct)
    {
        using var call = _client.GetResaleLockAsync(request, CreateCallOptions(ct));
        return await call.ResponseAsync;
    }

    public async Task<VerificationView> Close(CloseVerificationRequest request, CancellationToken ct)
    {
        using var call = _client.CloseVerificationAsync(request, CreateCallOptions(ct));
        return await call.ResponseAsync;
    }

    public async Task<ReleaseResaleLockResponse> Release(ReleaseResaleLockRequest request, CancellationToken ct)
    {
        using var call = _client.ReleaseResaleLockAsync(request, CreateCallOptions(ct));
        return await call.ResponseAsync;
    }
}
