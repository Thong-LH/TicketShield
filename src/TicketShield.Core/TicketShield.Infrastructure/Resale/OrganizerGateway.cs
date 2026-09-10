using Grpc.Core;
using TicketShield.Contracts.Organizer.V1;

namespace TicketShield.Infrastructure.Resale;

public sealed class OrganizerConnectionOptions
{
    public bool Enabled { get; set; }
    public string Address { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string HmacKey { get; set; } = "";
    public string OrganizerId { get; set; } = "";
    public int DeadlineSeconds { get; set; } = 15;
    public Dictionary<string, CoreTicketMapping> Mappings { get; set; } = new(StringComparer.Ordinal);
}
public sealed class CoreTicketMapping
{
    public string ExternalEventId { get; set; } = "";
    public string ExternalTierId { get; set; } = "";
    public Guid EventId { get; set; }
    public Guid TierId { get; set; }
}

public sealed class OrganizerGateway(OrganizerResaleService.OrganizerResaleServiceClient client, OrganizerConnectionOptions options)
{
    private CallOptions Call(CancellationToken ct) => new(new Metadata { { "authorization", "Bearer " + options.ApiKey } },
        DateTime.UtcNow.AddSeconds(options.DeadlineSeconds), ct);
    public async Task<ChallengeView> Request(RequestTicketOtpRequest r, CancellationToken ct) { using var call = client.RequestTicketOtpAsync(r, Call(ct)); return await call.ResponseAsync; }
    public async Task<ChallengeView> Resend(ResendTicketOtpRequest r, CancellationToken ct) { using var call = client.ResendTicketOtpAsync(r, Call(ct)); return await call.ResponseAsync; }
    public async Task<VerificationReceipt> Confirm(ConfirmOtpAndLockRequest r, CancellationToken ct) { using var call = client.ConfirmOtpAndLockAsync(r, Call(ct)); return await call.ResponseAsync; }
    public async Task<OperationView> Operation(GetOperationRequest r, CancellationToken ct) { using var call = client.GetOperationAsync(r, Call(ct)); return await call.ResponseAsync; }
    public async Task<VerificationView> Verification(GetVerificationRequest r, CancellationToken ct) { using var call = client.GetVerificationAsync(r, Call(ct)); return await call.ResponseAsync; }
    public async Task<ResaleLockView> Lock(GetResaleLockRequest r, CancellationToken ct) { using var call = client.GetResaleLockAsync(r, Call(ct)); return await call.ResponseAsync; }
    public async Task<VerificationView> Close(CloseVerificationRequest r, CancellationToken ct) { using var call = client.CloseVerificationAsync(r, Call(ct)); return await call.ResponseAsync; }
    public async Task<ReleaseResaleLockResponse> Release(ReleaseResaleLockRequest r, CancellationToken ct) { using var call = client.ReleaseResaleLockAsync(r, Call(ct)); return await call.ResponseAsync; }
}
