using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Enum = System.Enum;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using MockOrganizer.API.Entities;
using TicketShield.Contracts;
using TicketShield.Contracts.Organizer.V1;

namespace MockOrganizer.API.Resale;

public sealed class OrganizerResaleGrpcService(ResaleStore db, ResaleOptions options, IOtpDelivery delivery,
    TimeProvider clock, IHostEnvironment environment) : OrganizerResaleService.OrganizerResaleServiceBase
{
    private const string Caller = "ticketshield-core"; // This credential authorizes exactly one caller.
    private DateTimeOffset Now => clock.GetUtcNow();
    private static RpcException Fail(string code, StatusCode status = StatusCode.FailedPrecondition) => ResaleErrors.Fail(status, code);
    private string Hash(string value) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.HmacKey), Encoding.UTF8.GetBytes(value)));
    private string Owner(MockTicket ticket) => Hash($"{ticket.Id}|{ticket.OwnerEmail}|{ticket.OwnerPhone}|{ticket.OwnerName}");
    private static string SessionKey(string id) => "session:" + id;
    private static string LockKey(string id) => "lock:" + id;
    private static string OpKey(OperationKind kind, string id) => $"op:{Caller}:{kind}:{id}";
    private static long ComputeLockKey(string? target)
    {
        if (string.IsNullOrEmpty(target)) return 84722001L;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("mo:resale:" + target));
        return BitConverter.ToInt64(hash, 0);
    }
    private static void Uuid(string? value)
    {
        if (!Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty || value != id.ToString("D"))
            throw Fail("INVALID_REFERENCE", StatusCode.InvalidArgument);
    }
    private void Authenticate(ServerCallContext context)
    {
        var http = context.GetHttpContext();
        if (!http.Request.IsHttps && !(environment.IsDevelopment() && http.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip)))
            throw Fail("TLS_REQUIRED", StatusCode.Unauthenticated);
        var supplied = context.RequestHeaders.Where(x => x.Key == "authorization").ToArray();
        if (supplied.Length != 1 || !CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied[0].Value)),
            SHA256.HashData(Encoding.UTF8.GetBytes("Bearer " + options.ApiKey)) ))
            throw Fail("CALLER_UNAUTHENTICATED", StatusCode.Unauthenticated);
    }
    private static void Validate(OperationContext? op)
    {
        if (op is null) throw Fail("MISSING_OPERATION", StatusCode.InvalidArgument);
        Uuid(op.OperationId); Uuid(op.VerificationId); Uuid(op.RequesterRef);
    }
    private static void Validate(VerificationReference? v)
    {
        if (v is null) throw Fail("MISSING_REFERENCE", StatusCode.InvalidArgument);
        Uuid(v.VerificationId); Uuid(v.RequesterRef);
    }
    private async Task<SessionState> Session(string id, string requester, CancellationToken ct)
    {
        var session = await db.Read<SessionState>(SessionKey(id), ct) ?? throw Fail("VERIFICATION_NOT_FOUND", StatusCode.NotFound);
        if (session.Caller != Caller || session.Requester != requester) throw Fail("VERIFICATION_ACCESS_DENIED", StatusCode.PermissionDenied);
        return session;
    }
    private async Task<MockTicket> Ticket(string code, CancellationToken ct)
    {
        var tickets = await db.Tickets.FromSqlInterpolated($"SELECT * FROM mock_tickets WHERE ticket_code = {code} FOR UPDATE").ToListAsync(ct);
        return tickets.SingleOrDefault() ?? throw Fail("TICKET_NOT_FOUND", StatusCode.NotFound);
    }
    private async Task Quota(string key, int max, CancellationToken ct, bool increment = true)
    {
        var q = await db.Read<Quota>(key, ct) ?? new Quota { Since = Now };
        if (Now - q.Since >= TimeSpan.FromHours(1)) q = new Quota { Since = Now };
        if (q.Count >= max) throw Fail("OTP_RATE_LIMITED", StatusCode.ResourceExhausted);
        if (increment) { q.Count++; await db.Put(key, q, ct); }
    }
    private async Task<T> Mutate<T>(IMessage request, OperationContext op, OperationKind kind, ServerCallContext context, Func<Task<T>> action)
        where T : class, IMessage<T>, new()
    {
        Authenticate(context); Validate(op);
        var ct = context.CancellationToken;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        long lockKey = ComputeLockKey(op.VerificationId ?? op.OperationId);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", ct);
        var key = OpKey(kind, op.OperationId);
        var fingerprint = Hash(Convert.ToBase64String(request.ToByteArray()));
        var previous = await db.Read<OperationRecord>(key, ct);
        if (previous is not null)
        {
            if (previous.Fingerprint != fingerprint) throw Fail("IDEMPOTENCY_CONFLICT", StatusCode.AlreadyExists);
            if (previous.Error is not null) throw Fail(previous.Error, (StatusCode)previous.Status);
            return JsonParser.Default.Parse<T>(previous.ResponseJson);
        }
        T? result = null;
        RpcException? failure = null;
        try { result = await action(); }
        catch (RpcException ex) { failure = ex; }
        await db.Put(key, new OperationRecord {
            Fingerprint = fingerprint, SessionId = op.VerificationId ?? string.Empty, Requester = op.RequesterRef ?? string.Empty,
            ResponseJson = result is null ? "" : JsonFormatter.Default.Format(result),
            Error = failure?.Status.Detail, Status = (int)(failure?.StatusCode ?? StatusCode.OK)
        }, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct); // Includes failed-attempt counters and terminal rejection receipts.
        if (failure is not null) throw failure;
        return result!;
    }
    private ChallengeView View(SessionState s) => new() {
        VerificationId = s.Id, ChallengeId = s.ChallengeId, Generation = s.Generation,
        State = s.ReceiptJson is not null ? ChallengeState.Consumed : s.Closed || s.Failures >= options.MaxAttempts ? ChallengeState.Blocked : Now >= s.ExpiresAt ? ChallengeState.Expired : ChallengeState.Active,
        ExpiresAt = Timestamp.FromDateTimeOffset(s.ExpiresAt), ResendAfter = Timestamp.FromDateTimeOffset(s.ResendAfter),
        DeliveryState = Enum.Parse<DeliveryState>(s.Delivery)
    };
    private ResaleLockView View(LockRecord l) => new() {
        ResaleLock = new LockReference { LockId = l.Id, Generation = l.Generation },
        Verification = new VerificationReference { VerificationId = l.SessionId, RequesterRef = l.Requester },
        Ticket = new TicketReference { OrganizerId = options.OrganizerId, TicketCode = l.TicketCode },
        State = l.ReleasedAt.HasValue ? LockState.Released : LockState.Held,
        AcquiredAt = Timestamp.FromDateTimeOffset(l.AcquiredAt),
        ReleasedAt = l.ReleasedAt.HasValue ? Timestamp.FromDateTimeOffset(l.ReleasedAt.Value) : null
    };
    private async Task<VerificationView> ViewSession(SessionState s, CancellationToken ct)
    {
        var view = new VerificationView {
            Verification = new VerificationReference { VerificationId = s.Id, RequesterRef = s.Requester },
            State = s.Closed ? VerificationState.Closed : s.ReceiptJson is not null ? VerificationState.Verified : VerificationState.PendingOtp,
            Challenge = s.Generation > 0 ? View(s) : null,
            Receipt = s.ReceiptJson is null ? null : JsonParser.Default.Parse<VerificationReceipt>(s.ReceiptJson)
        };
        if (s.LockId is not null && await db.Read<LockRecord>(LockKey(s.LockId), ct) is { } l) view.CurrentLock = View(l);
        return view;
    }
    private string Issue(SessionState s)
    {
        var otp = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        s.Generation++; s.ChallengeId = Guid.NewGuid().ToString("D");
        s.OtpVerifier = Hash($"{s.Id}|{s.Generation}|{otp}");
        s.ExpiresAt = Now.AddSeconds(options.OtpLifetimeSeconds);
        s.ResendAfter = Now.AddSeconds(options.ResendCooldownSeconds); s.Delivery = "Pending";
        return otp;
    }
    // Delivery is outside the DB transaction. An uncertain SMTP response is never labelled delivered.
    private async Task<ChallengeView> Deliver(ChallengeView result, string? recipient, string? otp, OperationContext op, OperationKind kind, CancellationToken ct)
    {
        if (otp is null || recipient is null) return result; // Replay does not send another email.
        var state = "SmtpAccepted";
        try { await delivery.Send(recipient, otp, result.ExpiresAt.ToDateTimeOffset(), ct); }
        catch (InvalidOperationException) { state = "Failed"; }
        catch (System.Net.Mail.SmtpFailedRecipientException) { state = "Failed"; }
        catch (Exception) { state = "Unknown"; } // No exception/payload logging: SMTP errors may contain addresses.
        await using var tx = await db.Database.BeginTransactionAsync(CancellationToken.None);
        long lockKey = ComputeLockKey(op.VerificationId ?? op.OperationId);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", CancellationToken.None);
        db.ChangeTracker.Clear();
        var s = await Session(op.VerificationId, op.RequesterRef, CancellationToken.None);
        if (s.Generation == result.Generation) { s.Delivery = state; await db.Put(SessionKey(s.Id), s, CancellationToken.None); }
        result.DeliveryState = Enum.Parse<DeliveryState>(state);
        var key = OpKey(kind, op.OperationId);
        var operation = (await db.Read<OperationRecord>(key, CancellationToken.None))!;
        operation.ResponseJson = JsonFormatter.Default.Format(result);
        await db.Put(key, operation, CancellationToken.None);
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return result;
    }
    public override async Task<ChallengeView> RequestTicketOtp(RequestTicketOtpRequest request, ServerCallContext context)
    {
        string? otp = null, recipient = null;
        var result = await Mutate(request, request.Operation, OperationKind.RequestOtp, context, async () => {
            var ct = context.CancellationToken;
            if (request.Ticket is null || request.Ticket.OrganizerId != options.OrganizerId || string.IsNullOrWhiteSpace(request.Ticket.TicketCode) || request.Ticket.TicketCode.Length > 256)
                throw Fail("INVALID_TICKET_REFERENCE", StatusCode.InvalidArgument);
            var op = request.Operation;
            if (await db.Read<SessionState>(SessionKey(op.VerificationId), ct) is not null) throw Fail("VERIFICATION_ALREADY_EXISTS");
            var ticket = await Ticket(request.Ticket.TicketCode, ct);
            if (ticket.Status != "VALID") throw Fail("TICKET_NOT_AVAILABLE");
            if (!options.TicketMappings.TryGetValue(ticket.TicketCode, out var mapping) || string.IsNullOrEmpty(mapping.EventId) || string.IsNullOrEmpty(mapping.TierId))
                throw Fail("TICKET_MAPPING_NOT_CONFIGURED");
            await Quota("request-ticket:" + Hash(ticket.TicketCode), options.RequestsPerTicketPerHour, ct);
            await Quota("request-actor:" + op.RequesterRef, options.RequestsPerRequesterPerHour, ct);
            var s = new SessionState { Id = op.VerificationId, Caller = Caller, Requester = op.RequesterRef, TicketCode = ticket.TicketCode, OwnerRevision = Owner(ticket) };
            otp = Issue(s); recipient = ticket.OwnerEmail;
            await db.Put(SessionKey(s.Id), s, ct);
            return View(s);
        });
        return await Deliver(result, recipient, otp, request.Operation, OperationKind.RequestOtp, context.CancellationToken);
    }
    public override async Task<ChallengeView> ResendTicketOtp(ResendTicketOtpRequest request, ServerCallContext context)
    {
        string? otp = null, recipient = null;
        var result = await Mutate(request, request.Operation, OperationKind.ResendOtp, context, async () => {
            var ct = context.CancellationToken;
            var s = await Session(request.Operation.VerificationId, request.Operation.RequesterRef, ct);
            CheckChallenge(s, request.ChallengeId, request.ExpectedGeneration);
            if (s.ReceiptJson is not null || s.Closed || s.Failures >= options.MaxAttempts) throw Fail("VERIFICATION_CLOSED");
            if (Now < s.ResendAfter || s.Resends >= options.MaxResends) throw Fail("OTP_RATE_LIMITED", StatusCode.ResourceExhausted);
            var t = await Ticket(s.TicketCode, ct);
            if (t.Status != "VALID" || Owner(t) != s.OwnerRevision) throw Fail("TICKET_CHANGED");
            s.Resends++; otp = Issue(s); recipient = t.OwnerEmail;
            await db.Put(SessionKey(s.Id), s, ct); return View(s);
        });
        return await Deliver(result, recipient, otp, request.Operation, OperationKind.ResendOtp, context.CancellationToken);
    }
    private static void CheckChallenge(SessionState s, string challenge, uint generation)
    {
        if (s.ChallengeId != challenge || generation == 0 || s.Generation != generation) throw Fail("CHALLENGE_SUPERSEDED");
    }
    public override Task<VerificationReceipt> ConfirmOtpAndLock(ConfirmOtpAndLockRequest request, ServerCallContext context) =>
        Mutate(request, request.Operation, OperationKind.ConfirmAndLock, context, async () => {
            var ct = context.CancellationToken;
            var s = await Session(request.Operation.VerificationId, request.Operation.RequesterRef, ct);
            CheckChallenge(s, request.ChallengeId, request.ExpectedGeneration);
            if (s.Closed || s.ReceiptJson is not null) throw Fail("VERIFICATION_CLOSED");
            if (s.Failures >= options.MaxAttempts) throw Fail("OTP_ATTEMPTS_EXHAUSTED", StatusCode.ResourceExhausted);
            if (Now >= s.ExpiresAt) throw Fail("OTP_EXPIRED");
            await Quota("failure-ticket:" + Hash(s.TicketCode), options.FailuresPerTicketPerHour, ct, false);
            await Quota("failure-actor:" + s.Requester, options.FailuresPerRequesterPerHour, ct, false);
            if (request.Otp.Length != 6 || !request.Otp.All(c => c is >= '0' and <= '9') ||
                !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(s.OtpVerifier), Encoding.ASCII.GetBytes(Hash($"{s.Id}|{s.Generation}|{request.Otp}"))))
            {
                s.Failures++; await db.Put(SessionKey(s.Id), s, ct);
                await Quota("failure-ticket:" + Hash(s.TicketCode), options.FailuresPerTicketPerHour, ct);
                await Quota("failure-actor:" + s.Requester, options.FailuresPerRequesterPerHour, ct);
                throw Fail("OTP_INVALID");
            }
            var t = await Ticket(s.TicketCode, ct);
            if (t.Status != "VALID" || Owner(t) != s.OwnerRevision) throw Fail("TICKET_CHANGED");
            if (t.OriginalPrice < 0 || t.OriginalPrice > 9_999_999_999_999m || decimal.Truncate(t.OriginalPrice) != t.OriginalPrice) throw Fail("INVALID_ORIGINAL_PRICE");
            if (!options.TicketMappings.TryGetValue(t.TicketCode, out var mapping)) throw Fail("TICKET_MAPPING_NOT_CONFIGURED");
            var current = await db.Read<LockRecord>("current:" + Hash(s.TicketCode), ct);
            if (current is not null && current.ReleasedAt is null) throw Fail("TICKET_LOCKED");
            var l = new LockRecord { Id = Guid.NewGuid().ToString("D"), SessionId = s.Id, Caller = Caller, Requester = s.Requester,
                TicketCode = s.TicketCode, OwnerRevision = s.OwnerRevision, Generation = checked((current?.Generation ?? 0) + 1), AcquiredAt = Now };
            t.Status = "LOCKED_FOR_RESALE"; t.UpdatedAt = Now;
            var receipt = new VerificationReceipt {
                ReceiptId = Guid.NewGuid().ToString("D"), Operation = request.Operation.Clone(), ChallengeId = s.ChallengeId, ChallengeGeneration = s.Generation,
                Ticket = new TicketSnapshot { Ticket = new TicketReference { OrganizerId = options.OrganizerId, TicketCode = t.TicketCode }, TicketId = t.Id.ToString("D"),
                    OwnerRevision = s.OwnerRevision, TicketRevision = Hash($"{t.Id}|{t.UpdatedAt:O}|{t.Status}"), OriginalPrice = decimal.ToInt64(t.OriginalPrice),
                    ExternalEventId = mapping.EventId, ExternalTierId = mapping.TierId, EventName = t.EventName, SeatZone = t.SeatZone },
                ResaleLock = new LockReference { LockId = l.Id, Generation = l.Generation }, VerifiedAt = Timestamp.FromDateTimeOffset(Now)
            };
            s.ReceiptJson = JsonFormatter.Default.Format(receipt); s.LockId = l.Id; s.OtpVerifier = "";
            await db.Put(SessionKey(s.Id), s, ct); await db.Put(LockKey(l.Id), l, ct); await db.Put("current:" + Hash(s.TicketCode), l, ct);
            return receipt;
        });
    public override Task<ReleaseResaleLockResponse> ReleaseResaleLock(ReleaseResaleLockRequest request, ServerCallContext context) =>
        Mutate(request, request.Operation, OperationKind.ReleaseLock, context, async () => {
            var ct = context.CancellationToken;
            if (request.Reason is not (ReleaseReason.ListingCancelled or ReleaseReason.VerificationAbandoned)) throw Fail("INVALID_RELEASE_REASON", StatusCode.InvalidArgument);
            var s = await Session(request.Operation.VerificationId, request.Operation.RequesterRef, ct);
            var l = await db.Read<LockRecord>(LockKey(request.LockId), ct) ?? throw Fail("LOCK_NOT_FOUND", StatusCode.NotFound);
            if (l.Caller != Caller || l.SessionId != s.Id || l.Requester != s.Requester || l.Generation != request.ExpectedLockGeneration)
                throw Fail("LOCK_GENERATION_CONFLICT", StatusCode.Aborted);
            if (l.ReleasedAt.HasValue) return new ReleaseResaleLockResponse { Outcome = ReleaseOutcome.AlreadyReleased, ResaleLock = View(l) };
            var t = await Ticket(l.TicketCode, ct);
            var current = await db.Read<LockRecord>("current:" + Hash(l.TicketCode), ct);
            if (current?.Id != l.Id || t.Status != "LOCKED_FOR_RESALE" || Owner(t) != l.OwnerRevision) throw Fail("TICKET_CHANGED");
            t.Status = "VALID"; t.UpdatedAt = Now; l.ReleasedAt = Now; s.Closed = true;
            await db.Put(LockKey(l.Id), l, ct); await db.Put("current:" + Hash(l.TicketCode), l, ct); await db.Put(SessionKey(s.Id), s, ct);
            return new ReleaseResaleLockResponse { Outcome = ReleaseOutcome.Released, ResaleLock = View(l) };
        });
    public override Task<VerificationView> CloseVerification(CloseVerificationRequest request, ServerCallContext context) =>
        Mutate(request, request.Operation, OperationKind.CloseVerification, context, async () => {
            var ct = context.CancellationToken;
            var s = await db.Read<SessionState>(SessionKey(request.Operation.VerificationId), ct);
            if (s is null) s = new SessionState { Id = request.Operation.VerificationId, Caller = Caller, Requester = request.Operation.RequesterRef };
            else if (s.Caller != Caller || s.Requester != request.Operation.RequesterRef) throw Fail("VERIFICATION_ACCESS_DENIED", StatusCode.PermissionDenied);
            s.Closed = true; s.OtpVerifier = ""; await db.Put(SessionKey(s.Id), s, ct); return await ViewSession(s, ct);
        });
    private async Task<T> Read<T>(VerificationReference v, ServerCallContext context, Func<Task<T>> action)
    {
        Authenticate(context); Validate(v);
        await using var tx = await db.Database.BeginTransactionAsync(context.CancellationToken);
        long lockKey = ComputeLockKey(v.VerificationId);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", context.CancellationToken);
        return await action();
    }
    public override Task<VerificationView> GetVerification(GetVerificationRequest r, ServerCallContext c) =>
        Read(r.Verification, c, async () => await ViewSession(await Session(r.Verification.VerificationId, r.Verification.RequesterRef, c.CancellationToken), c.CancellationToken));
    public override Task<ResaleLockView> GetResaleLock(GetResaleLockRequest r, ServerCallContext c) =>
        Read(r.Verification, c, async () => {
            var s = await Session(r.Verification.VerificationId, r.Verification.RequesterRef, c.CancellationToken);
            var l = await db.Read<LockRecord>(LockKey(r.LockId), c.CancellationToken) ?? throw Fail("LOCK_NOT_FOUND", StatusCode.NotFound);
            if (l.Caller != Caller || l.SessionId != s.Id || l.Requester != s.Requester) throw Fail("LOCK_ACCESS_DENIED", StatusCode.PermissionDenied);
            return View(l);
        });
    public override Task<OperationView> GetOperation(GetOperationRequest r, ServerCallContext c) => Read(r.Verification, c, async () => {
        Uuid(r.OperationId);
        if (r.Kind == OperationKind.Unspecified || !Enum.IsDefined(r.Kind)) throw Fail("INVALID_OPERATION_KIND", StatusCode.InvalidArgument);
        var o = await db.Read<OperationRecord>(OpKey(r.Kind, r.OperationId), c.CancellationToken) ?? throw Fail("OPERATION_NOT_FOUND", StatusCode.NotFound);
        if (o.SessionId != r.Verification.VerificationId || o.Requester != r.Verification.RequesterRef) throw Fail("OPERATION_ACCESS_DENIED", StatusCode.PermissionDenied);
        var view = new OperationView { Kind = r.Kind, State = o.Error is null ? OperationState.Succeeded : OperationState.Rejected,
            Operation = new OperationContext { OperationId = r.OperationId, VerificationId = o.SessionId, RequesterRef = o.Requester } };
        if (o.Error is not null) view.Rejection = new BusinessError { Code = o.Error };
        else switch (r.Kind) {
            case OperationKind.RequestOtp: case OperationKind.ResendOtp: view.Challenge = JsonParser.Default.Parse<ChallengeView>(o.ResponseJson); break;
            case OperationKind.ConfirmAndLock: view.Receipt = JsonParser.Default.Parse<VerificationReceipt>(o.ResponseJson); break;
            case OperationKind.ReleaseLock: view.Release = JsonParser.Default.Parse<ReleaseResaleLockResponse>(o.ResponseJson); break;
            case OperationKind.CloseVerification: view.ClosedVerification = JsonParser.Default.Parse<VerificationView>(o.ResponseJson); break;
        }
        return view;
    });
}
