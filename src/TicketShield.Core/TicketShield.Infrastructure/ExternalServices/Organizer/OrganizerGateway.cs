using Grpc.Core;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Contracts.Organizer.V1;

namespace TicketShield.Infrastructure.ExternalServices.Organizer;

/// <summary>
/// Client gRPC giao tiếp thực tế với hệ thống Nhà tổ chức (Organizer System)
/// </summary>
public sealed class OrganizerGateway : IOrganizerGateway
{
    private readonly OrganizerResaleService.OrganizerResaleServiceClient _defaultClient;
    private readonly OrganizerConnectionOptions _options;
    private readonly IOrganizerGatewayResolver _resolver;

    public OrganizerGateway(
        OrganizerResaleService.OrganizerResaleServiceClient defaultClient,
        OrganizerConnectionOptions options,
        IOrganizerGatewayResolver resolver)
    {
        _defaultClient = defaultClient;
        _options = options;
        _resolver = resolver;
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
        var orgId = Guid.TryParse(request.Ticket?.OrganizerId, out var id) ? id : (Guid?)null;
        var client = await _resolver.ResolveClientAsync(orgId, ct);
        using var call = client.RequestTicketOtpAsync(request, CreateCallOptions(ct));
        return await call.ResponseAsync;
    }

    public async Task<ChallengeView> Resend(ResendTicketOtpRequest request, CancellationToken ct)
    {
        using var call = _defaultClient.ResendTicketOtpAsync(request, CreateCallOptions(ct));
        return await call.ResponseAsync;
    }

    public async Task<VerificationReceipt> Confirm(ConfirmOtpAndLockRequest request, CancellationToken ct)
    {
        using var call = _defaultClient.ConfirmOtpAndLockAsync(request, CreateCallOptions(ct));
        return await call.ResponseAsync;
    }

    public async Task<OperationView> Operation(GetOperationRequest request, CancellationToken ct)
    {
        using var call = _defaultClient.GetOperationAsync(request, CreateCallOptions(ct));
        return await call.ResponseAsync;
    }

    public async Task<VerificationView> Verification(GetVerificationRequest request, CancellationToken ct)
    {
        using var call = _defaultClient.GetVerificationAsync(request, CreateCallOptions(ct));
        return await call.ResponseAsync;
    }

    public async Task<ResaleLockView> Lock(GetResaleLockRequest request, CancellationToken ct)
    {
        using var call = _defaultClient.GetResaleLockAsync(request, CreateCallOptions(ct));
        return await call.ResponseAsync;
    }

    public async Task<VerificationView> Close(CloseVerificationRequest request, CancellationToken ct)
    {
        using var call = _defaultClient.CloseVerificationAsync(request, CreateCallOptions(ct));
        return await call.ResponseAsync;
    }

    public async Task<ReleaseResaleLockResponse> Release(ReleaseResaleLockRequest request, CancellationToken ct)
    {
        using var call = _defaultClient.ReleaseResaleLockAsync(request, CreateCallOptions(ct));
        return await call.ResponseAsync;
    }

    public async Task<TransferOwnershipResponse> TransferOwnership(TransferOwnershipRequest request, CancellationToken ct)
    {
        using var call = _defaultClient.TransferOwnershipAsync(request, CreateCallOptions(ct));
        return await call.ResponseAsync;
    }
}
