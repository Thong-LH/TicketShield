using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Resale;
using TicketShield.Contracts;
using TicketShield.Contracts.Organizer.V1;
using TicketShield.Infrastructure.ExternalServices.Organizer;
using TicketShield.Infrastructure.Persistence.Resale;

namespace TicketShield.Infrastructure.Services;

/// <summary>
/// Triển khai dịch vụ xác thực vé qua gRPC và quản lý tin bán lại (Ticket Verification & Lock Saga).
/// </summary>
public class TicketVerificationService : ITicketVerificationService, ITicketResaleWorkflow
{
    private readonly CoreResaleStore _db;
    private readonly IOrganizerGateway _gateway;
    private readonly OrganizerConnectionOptions _options;
    private readonly TimeProvider _clock;

    public TicketVerificationService(
        CoreResaleStore db,
        IOrganizerGateway gateway,
        OrganizerConnectionOptions options,
        TimeProvider clock)
    {
        _db = db;
        _gateway = gateway;
        _options = options;
        _clock = clock;
    }

    private static ResaleWorkflowException Error(string code, int status = 409) => new(code, status);

    private static string SessionKey(string id) => "session:" + id;

    private static string OpKey(string actor, string kind, string id) => $"op:{actor}:{kind}:{id}";

    private string Fingerprint(object value)
    {
        var key = Encoding.UTF8.GetBytes(_options.HmacKey);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        return Convert.ToHexString(HMACSHA256.HashData(key, bytes));
    }

    private static void ValidateUuid(string id)
    {
        if (!Guid.TryParseExact(id, "D", out var guid) || guid == Guid.Empty || guid.ToString("D") != id)
        {
            throw Error("INVALID_REFERENCE", 400);
        }
    }

    private static long ComputeLockKey(string? target)
    {
        if (string.IsNullOrEmpty(target))
        {
            return 84722002L;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("ts:resale:" + target));
        return BitConverter.ToInt64(hash, 0);
    }

    private async Task<T> Transaction<T>(Func<Task<T>> action, CancellationToken ct, string? lockTarget = null)
    {
        _db.ChangeTracker.Clear();
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        long lockKey = ComputeLockKey(lockTarget);
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", ct);

        var result = await action();
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }

    private async Task<CoreSession> Owned(string seller, string id, CancellationToken ct)
    {
        ValidateUuid(seller);
        ValidateUuid(id);

        var session = await _db.Read<CoreSession>(SessionKey(id), ct)
            ?? throw Error("VERIFICATION_NOT_FOUND", 404);

        if (session.Seller != seller)
        {
            throw Error("VERIFICATION_NOT_FOUND", 404);
        }

        return session;
    }

    private static OperationContext Context(CoreSession s, CoreOperation op) => new()
    {
        OperationId = op.Id,
        VerificationId = s.Id,
        RequesterRef = s.Seller
    };

    private static VerificationReference Reference(CoreSession s) => new()
    {
        VerificationId = s.Id,
        RequesterRef = s.Seller
    };

    private static ChallengeView Challenge(CoreSession s) =>
        s.ChallengeJson is not null
            ? JsonParser.Default.Parse<ChallengeView>(s.ChallengeJson)
            : throw Error("CHALLENGE_NOT_READY");

    private static VerificationReceipt Receipt(CoreSession s) =>
        s.ReceiptJson is not null
            ? JsonParser.Default.Parse<VerificationReceipt>(s.ReceiptJson)
            : throw Error("VERIFICATION_NOT_READY");

    private VerificationResult Result(CoreSession s)
    {
        var challenge = s.ChallengeJson is null ? null : Challenge(s);
        var receipt = s.ReceiptJson is null ? null : Receipt(s);
        var isExpired = s.State == "OtpReady" && challenge?.ExpiresAt?.ToDateTimeOffset() <= _clock.GetUtcNow();
        var state = isExpired ? "OtpExpired" : s.State;

        return new VerificationResult(
            VerificationId: s.Id,
            Status: state,
            OperationId: s.PendingOperationId?.Split(':').Last(),
            ExpiresAt: challenge?.ExpiresAt?.ToDateTimeOffset(),
            ResendAfter: challenge?.ResendAfter?.ToDateTimeOffset(),
            DeliveryState: challenge?.DeliveryState.ToString(),
            OriginalPrice: receipt?.Ticket?.HasOriginalPrice == true ? receipt.Ticket.OriginalPrice : null,
            ListingId: s.ListingId,
            PrivateAccessToken: s.PrivateAccessToken
        );
    }

    private async Task<(CoreSession Session, CoreOperation Operation)> Begin(
        string seller,
        string operationId,
        string kind,
        string? sessionId,
        object payload,
        string? ticket,
        CancellationToken ct)
    {
        ValidateUuid(seller);
        ValidateUuid(operationId);

        var fingerprint = Fingerprint(new { sessionId, payload });

        return await Transaction(async () =>
        {
            var key = OpKey(seller, kind, operationId);
            var previous = await _db.Read<CoreOperation>(key, ct);
            if (previous is not null)
            {
                if (previous.Fingerprint != fingerprint)
                {
                    throw Error("IDEMPOTENCY_CONFLICT");
                }
                if (previous.State is "Rejected" or "Superseded")
                {
                    throw Error(previous.Error ?? "OPERATION_REJECTED");
                }
                return (await Owned(seller, previous.SessionId, ct), previous);
            }

            var sellerId = Guid.Parse(seller);
            var isActive = await _db.Database
                .SqlQuery<int>($"SELECT 1 AS \"Value\" FROM users WHERE id={sellerId} AND is_active=true")
                .AnyAsync(ct);

            if (!isActive)
            {
                throw Error("SELLER_NOT_ACTIVE", 403);
            }

            CoreSession session;
            if (kind == "Request")
            {
                if (string.IsNullOrWhiteSpace(ticket) || ticket.Length > 256)
                {
                    throw Error("INVALID_TICKET_CODE", 400);
                }
                session = new CoreSession
                {
                    Id = Guid.NewGuid().ToString("D"),
                    Seller = seller,
                    TicketCode = ticket
                };
            }
            else
            {
                session = await Owned(seller, sessionId!, ct);
                if (session.PendingOperationId is not null)
                {
                    throw Error("OPERATION_PENDING");
                }

                var allowed = kind switch
                {
                    "Resend" or "Confirm" => session.State == "OtpReady",
                    "Publish" => session.State == "Verified",
                    "Close" => session.State is "OtpReady" or "Verified" or "Rejected",
                    "Cancel" => session.State == "Published",
                    _ => false
                };

                if (!allowed)
                {
                    throw Error("INVALID_VERIFICATION_STATE");
                }
            }

            var op = new CoreOperation
            {
                Id = operationId,
                Kind = kind,
                SessionId = session.Id,
                Seller = seller,
                Fingerprint = fingerprint
            };

            if (payload is PublishBody publish)
            {
                session.Price = publish.ResalePrice;
            }

            session.State = kind + "Pending";
            session.PendingOperationId = key;
            session.UpdatedAt = _clock.GetUtcNow();

            await _db.Put(key, op, ct);
            await _db.Put(SessionKey(session.Id), session, ct);

            return (session, op);
        }, ct, ticket ?? sessionId);
    }

    public async Task<VerificationResult> Request(string seller, string key, string ticket, CancellationToken ct)
    {
        var (s, op) = await Begin(seller, key, "Request", null, new { ticket }, ticket, ct);
        return op.State == "Succeeded" ? Result(s) : await Execute(s, op, null, ct);
    }

    public async Task<VerificationResult> Resend(string seller, string id, string key, CancellationToken ct)
    {
        var (s, op) = await Begin(seller, key, "Resend", id, new { }, null, ct);
        return op.State == "Succeeded" ? Result(s) : await Execute(s, op, null, ct);
    }

    public async Task<VerificationResult> Confirm(string seller, string id, string key, string otp, CancellationToken ct)
    {
        if (otp is null || otp.Length > 32)
        {
            throw Error("INVALID_OTP_FORMAT", 400);
        }

        var (s, op) = await Begin(seller, key, "Confirm", id, new { otp }, null, ct);
        return op.State == "Succeeded" ? Result(s) : await Execute(s, op, otp, ct);
    }

    private static bool Uncertain(RpcException ex) =>
        ex.StatusCode is StatusCode.Unavailable
            or StatusCode.DeadlineExceeded
            or StatusCode.Cancelled
            or StatusCode.Internal
            or StatusCode.Unknown;

    private static string SafeCode(RpcException ex)
    {
        try
        {
            var trailer = ex.Trailers.FirstOrDefault(x => x.Key == ResaleErrors.Trailer);
            var code = trailer is null ? "ORGANIZER_REJECTED" : BusinessError.Parser.ParseFrom(trailer.ValueBytes).Code;
            return code.Length <= 80 && code.All(c => c is >= 'A' and <= 'Z' or '_') ? code : "ORGANIZER_REJECTED";
        }
        catch
        {
            return "ORGANIZER_REJECTED";
        }
    }

    private async Task<VerificationResult> Execute(CoreSession s, CoreOperation op, string? otp, CancellationToken ct)
    {
        try
        {
            IMessage response = op.Kind switch
            {
                "Request" => await _gateway.Request(new RequestTicketOtpRequest
                {
                    Operation = Context(s, op),
                    Ticket = new TicketReference { OrganizerId = _options.OrganizerId, TicketCode = s.TicketCode }
                }, ct),

                "Resend" => await _gateway.Resend(new ResendTicketOtpRequest
                {
                    Operation = Context(s, op),
                    ChallengeId = Challenge(s).ChallengeId,
                    ExpectedGeneration = Challenge(s).Generation
                }, ct),

                "Confirm" => await _gateway.Confirm(new ConfirmOtpAndLockRequest
                {
                    Operation = Context(s, op),
                    ChallengeId = Challenge(s).ChallengeId,
                    ExpectedGeneration = Challenge(s).Generation,
                    Otp = otp!
                }, ct),

                _ => throw Error("INVALID_OPERATION")
            };

            return await Complete(s, op, response, ct);
        }
        catch (RpcException ex) when (Uncertain(ex))
        {
            return Result(await Owned(s.Seller, s.Id, ct));
        }
        catch (RpcException ex)
        {
            var code = SafeCode(ex);
            if (op.Kind == "Request" && code is "TICKET_NOT_FOUND" or "TICKET_NOT_AVAILABLE")
            {
                code = "TICKET_NOT_ELIGIBLE";
            }

            await Reject(s, op, code, ct);
            var httpStatus = ex.StatusCode switch
            {
                StatusCode.ResourceExhausted => 429,
                StatusCode.InvalidArgument => 400,
                _ => 409
            };
            throw Error(code, httpStatus);
        }
    }

    private async Task Reject(CoreSession s, CoreOperation op, string code, CancellationToken ct) =>
        await Transaction(async () =>
        {
            var fresh = await Owned(s.Seller, s.Id, ct);
            var key = OpKey(s.Seller, op.Kind, op.Id);
            if (fresh.PendingOperationId != key)
            {
                return false;
            }

            op.State = "Rejected";
            op.Error = code;
            fresh.State = op.Kind switch
            {
                "Request" => "Rejected",
                "Confirm" or "Resend" => "OtpReady",
                "Publish" => "Verified",
                "Cancel" => "Published",
                _ => "NeedsReview"
            };
            fresh.PendingOperationId = null;

            await _db.Put(key, op, ct);
            await _db.Put(SessionKey(fresh.Id), fresh, ct);
            return true;
        }, ct, s.Id);

    private async Task<VerificationResult> Complete(CoreSession s, CoreOperation op, IMessage response, CancellationToken ct) =>
        await Transaction(async () =>
        {
            var fresh = await Owned(s.Seller, s.Id, ct);
            var key = OpKey(s.Seller, op.Kind, op.Id);
            if (fresh.PendingOperationId != key)
            {
                return Result(fresh);
            }

            if (response is ChallengeView challenge)
            {
                if (challenge.VerificationId != s.Id || challenge.Generation == 0 ||
                    string.IsNullOrEmpty(challenge.ChallengeId) ||
                    challenge.ExpiresAt is null || challenge.ResendAfter is null)
                {
                    throw Error("INVALID_ORGANIZER_RESPONSE", 502);
                }
                fresh.ChallengeJson = JsonFormatter.Default.Format(challenge);
                fresh.State = "OtpReady";
            }
            else if (response is VerificationReceipt receipt)
            {
                if (receipt.Operation is null || !receipt.Operation.Equals(Context(s, op)) ||
                    receipt.Ticket?.Ticket?.OrganizerId != _options.OrganizerId ||
                    receipt.Ticket.Ticket.TicketCode != s.TicketCode ||
                    receipt.ChallengeId != Challenge(s).ChallengeId ||
                    receipt.ChallengeGeneration != Challenge(s).Generation ||
                    !receipt.Ticket.HasOriginalPrice ||
                    receipt.ResaleLock is null ||
                    receipt.ResaleLock.Generation == 0 ||
                    string.IsNullOrEmpty(receipt.ResaleLock.LockId))
                {
                    throw Error("INVALID_ORGANIZER_RESPONSE", 502);
                }

                VndAmount.ToDatabase(receipt.Ticket.OriginalPrice);
                fresh.ReceiptJson = JsonFormatter.Default.Format(receipt);
                fresh.State = "Verified";
            }
            else
            {
                throw Error("INVALID_ORGANIZER_RESPONSE", 502);
            }

            op.State = "Succeeded";
            fresh.PendingOperationId = null;
            fresh.UpdatedAt = _clock.GetUtcNow();

            await _db.Put(key, op, ct);
            await _db.Put(SessionKey(fresh.Id), fresh, ct);
            return Result(fresh);
        }, ct, s.Id);

    public async Task<VerificationResult> Get(string seller, string id, CancellationToken ct)
    {
        var s = await Owned(seller, id, ct);
        if (s.PendingOperationId is null)
        {
            return Result(s);
        }

        var op = await _db.Read<CoreOperation>(s.PendingOperationId, ct);
        if (op is null)
        {
            throw Error("OPERATION_NOT_FOUND", 500);
        }

        return await Recover(s, op, ct);
    }

    private async Task<VerificationResult> Recover(CoreSession s, CoreOperation op, CancellationToken ct)
    {
        if (op.Kind is "Close" or "Cancel")
        {
            return await Release(s, op, ct);
        }
        if (op.Kind == "Publish")
        {
            return await PublishClaimed(s, op, s.Price!.Value, s.PrivateAccessToken != null, ct);
        }

        var kind = op.Kind switch
        {
            "Request" => OperationKind.RequestOtp,
            "Resend" => OperationKind.ResendOtp,
            _ => OperationKind.ConfirmAndLock
        };

        try
        {
            var result = await _gateway.Operation(new GetOperationRequest
            {
                Verification = Reference(s),
                OperationId = op.Id,
                Kind = kind
            }, ct);

            if (!result.Operation.Equals(Context(s, op)) || result.Kind != kind)
            {
                throw Error("INVALID_ORGANIZER_RESPONSE", 502);
            }
            if (result.State == OperationState.Rejected)
            {
                await Reject(s, op, result.Rejection?.Code ?? "ORGANIZER_REJECTED", ct);
                return Result(await Owned(s.Seller, s.Id, ct));
            }
            if (result.State == OperationState.Succeeded)
            {
                IMessage response = kind == OperationKind.ConfirmAndLock ? result.Receipt : result.Challenge;
                if (response is null)
                {
                    throw Error("INVALID_ORGANIZER_RESPONSE", 502);
                }
                return await Complete(s, op, response, ct);
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            if (op.Kind is "Request" or "Resend")
            {
                return await Execute(s, op, null, ct);
            }
        }
        catch (RpcException ex) when (Uncertain(ex))
        {
        }

        return Result(s);
    }

    public async Task<VerificationResult> Publish(string seller, string key, PublishBody body, CancellationToken ct)
    {
        if (body.ResalePrice <= 0 || body.ResalePrice > VndAmount.MaxDatabaseValue)
        {
            throw Error("INVALID_VND_PRICE", 422);
        }

        var (s, op) = await Begin(seller, key, "Publish", body.VerificationId, body, null, ct);
        if (op.State == "Succeeded")
        {
            return Result(s);
        }

        s = await Transaction(async () =>
        {
            var fresh = await Owned(seller, s.Id, ct);
            if (fresh.PendingOperationId == OpKey(seller, "Publish", key))
            {
                fresh.Price = body.ResalePrice;
                await _db.Put(SessionKey(fresh.Id), fresh, ct);
            }
            return fresh;
        }, ct, s.Id);

        return await PublishClaimed(s, op, body.ResalePrice, body.IsPrivate, ct);
    }

    private async Task<VerificationResult> PublishClaimed(CoreSession s, CoreOperation op, long price, bool isPrivate, CancellationToken ct)
    {
        try
        {
            var receipt = Receipt(s);
            if (price <= 0 || price > receipt.Ticket.OriginalPrice)
            {
                throw Error("PRICE_EXCEEDS_CEILING", 422);
            }

            var mappings = _options.Mappings.Values
                .Where(x => x.ExternalEventId == receipt.Ticket.ExternalEventId && x.ExternalTierId == receipt.Ticket.ExternalTierId)
                .ToArray();

            if (mappings.Length != 1)
            {
                throw Error("TICKET_MAPPING_NOT_CONFIGURED");
            }
            var mapping = mappings[0];

            var locked = await _gateway.Lock(new GetResaleLockRequest
            {
                Verification = Reference(s),
                LockId = receipt.ResaleLock.LockId
            }, ct);

            if (locked.State != LockState.Held ||
                !locked.ResaleLock.Equals(receipt.ResaleLock) ||
                !locked.Verification.Equals(Reference(s)) ||
                !locked.Ticket.Equals(receipt.Ticket.Ticket))
            {
                throw Error("LOCK_NOT_HELD");
            }

            return await Transaction(async () =>
            {
                var fresh = await Owned(s.Seller, s.Id, ct);
                if (fresh.PendingOperationId != OpKey(s.Seller, op.Kind, op.Id))
                {
                    return Result(fresh);
                }

                var organizer = Guid.Parse(_options.OrganizerId);
                var now = _clock.GetUtcNow();

                var eventAvailable = await _db.Database.SqlQuery<int>($@"
                    SELECT 1 AS ""Value"" 
                    FROM ticket_tiers t 
                    JOIN events e ON e.id = t.event_id 
                    WHERE t.id = {mapping.TierId} 
                      AND e.id = {mapping.EventId} 
                      AND e.organizer_id = {organizer} 
                      AND e.resale_deadline > {now} 
                      AND e.status = 'UPCOMING'
                ").AnyAsync(ct);

                if (!eventAvailable)
                {
                    throw Error("EVENT_NOT_AVAILABLE");
                }

                var id = Guid.NewGuid();
                var seller = Guid.Parse(s.Seller);
                var original = VndAmount.ToDatabase(receipt.Ticket.OriginalPrice);
                var resale = VndAmount.ToDatabase(price);
                string? privateToken = isPrivate
                    ? Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()
                    : null;

                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO resale_listings (
                        id, event_id, tier_id, seller_id, original_ticket_code, 
                        original_price, resale_price, is_private, private_access_token, 
                        verification_status, listing_status, created_at, updated_at
                    ) VALUES (
                        {id}, {mapping.EventId}, {mapping.TierId}, {seller}, {s.TicketCode}, 
                        {original}, {resale}, {isPrivate}, {privateToken}, 
                        'Verified', 'Verified', {now}, {now}
                    )
                ", ct);

                fresh.State = "Published";
                fresh.ListingId = id;
                fresh.PendingOperationId = null;
                fresh.Price = price;
                fresh.PrivateAccessToken = privateToken;
                op.State = "Succeeded";

                await _db.Put(SessionKey(fresh.Id), fresh, ct);
                await _db.Put(OpKey(s.Seller, op.Kind, op.Id), op, ct);
                return Result(fresh);
            }, ct, s.Id);
        }
        catch (RpcException ex) when (Uncertain(ex))
        {
            return Result(s);
        }
        catch (RpcException ex)
        {
            await Reject(s, op, SafeCode(ex), ct);
            throw Error(SafeCode(ex));
        }
        catch (ResaleWorkflowException ex)
        {
            await Reject(s, op, ex.Code, ct);
            throw;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await Reject(s, op, "TICKET_ALREADY_LISTED", ct);
            throw Error("TICKET_ALREADY_LISTED");
        }
    }

    public async Task<VerificationResult> Close(string seller, string id, string key, CancellationToken ct)
    {
        ValidateUuid(key);

        await Transaction(async () =>
        {
            var s = await Owned(seller, id, ct);
            if (s.State is "RequestPending" or "ConfirmPending" or "ResendPending")
            {
                if (s.PendingOperationId is not null && await _db.Read<CoreOperation>(s.PendingOperationId, ct) is { } old)
                {
                    old.State = "Superseded";
                    await _db.Put(s.PendingOperationId, old, ct);
                }
                s.PendingOperationId = null;
                s.State = "Rejected";
                await _db.Put(SessionKey(id), s, ct);
            }
            return true;
        }, ct, id);

        var (session, op) = await Begin(seller, key, "Close", id, new { }, null, ct);
        return op.State == "Succeeded" ? Result(session) : await Release(session, op, ct);
    }

    public async Task<VerificationResult> Cancel(string seller, string id, string key, CancellationToken ct)
    {
        var (s, op) = await Begin(seller, key, "Cancel", id, new { }, null, ct);
        return op.State == "Succeeded" ? Result(s) : await Release(s, op, ct);
    }

    private async Task<VerificationResult> Release(CoreSession s, CoreOperation op, CancellationToken ct)
    {
        try
        {
            if (op.Kind == "Cancel")
            {
                await Transaction(async () =>
                {
                    var fresh = await Owned(s.Seller, s.Id, ct);
                    if (fresh.State == "Closed")
                    {
                        return true;
                    }

                    var cancellable = await _db.Database
                        .SqlQuery<int>($"SELECT 1 AS \"Value\" FROM resale_listings WHERE id={s.ListingId} AND listing_status='Verified'")
                        .AnyAsync(ct);

                    if (!cancellable)
                    {
                        throw Error("LISTING_NOT_CANCELLABLE");
                    }
                    return true;
                }, ct, s.Id);
            }

            var closed = await _gateway.Close(new CloseVerificationRequest
            {
                Operation = Context(s, op)
            }, ct);

            if (!closed.Verification.Equals(Reference(s)) || closed.State != VerificationState.Closed)
            {
                throw Error("INVALID_ORGANIZER_RESPONSE", 502);
            }

            if (closed.CurrentLock is { State: LockState.Held } locked)
            {
                var released = await _gateway.Release(new ReleaseResaleLockRequest
                {
                    Operation = Context(s, op),
                    LockId = locked.ResaleLock.LockId,
                    ExpectedLockGeneration = locked.ResaleLock.Generation,
                    Reason = op.Kind == "Cancel" ? ReleaseReason.ListingCancelled : ReleaseReason.VerificationAbandoned
                }, ct);

                if (released.ResaleLock?.State != LockState.Released ||
                    !released.ResaleLock.ResaleLock.Equals(locked.ResaleLock))
                {
                    throw Error("INVALID_ORGANIZER_RESPONSE", 502);
                }
            }

            return await Transaction(async () =>
            {
                var fresh = await Owned(s.Seller, s.Id, ct);
                if (fresh.PendingOperationId != OpKey(s.Seller, op.Kind, op.Id))
                {
                    return Result(fresh);
                }

                if (fresh.ListingId.HasValue)
                {
                    var now = _clock.GetUtcNow();
                    var updated = await _db.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE resale_listings SET listing_status='Cancelled', updated_at={now} WHERE id={fresh.ListingId} AND listing_status='Verified'",
                        ct);

                    if (updated != 1)
                    {
                        throw Error("LISTING_NOT_CANCELLABLE");
                    }
                }

                fresh.State = "Closed";
                fresh.PendingOperationId = null;
                op.State = "Succeeded";

                await _db.Put(SessionKey(fresh.Id), fresh, ct);
                await _db.Put(OpKey(s.Seller, op.Kind, op.Id), op, ct);
                return Result(fresh);
            }, ct, s.Id);
        }
        catch (RpcException ex) when (Uncertain(ex))
        {
            return Result(s);
        }
        catch (RpcException)
        {
            return Result(s);
        }
        catch (ResaleWorkflowException ex)
        {
            await Reject(s, op, ex.Code, ct);
            throw;
        }
    }

    public async Task<List<ListingResult>> Marketplace(int page, int size, CancellationToken ct)
    {
        if (page < 1 || size is < 1 or > 100)
        {
            throw Error("INVALID_PAGINATION", 400);
        }

        var offset = checked((long)(page - 1) * size);
        var rows = await _db.Database.SqlQuery<MarketRow>($@"
            SELECT l.id AS ""Id"", l.event_id AS ""EventId"", l.tier_id AS ""TierId"", l.resale_price AS ""Price"" 
            FROM resale_listings l 
            JOIN core_resale_records r ON r.""Id"" LIKE 'session:%' AND r.""Json""->>'ListingId' = l.id::text 
            WHERE l.is_private = false 
              AND l.listing_status = 'Verified' 
              AND r.""Json""->>'State' = 'Published' 
            ORDER BY l.created_at DESC, l.id 
            LIMIT {size} OFFSET {offset}
        ").ToListAsync(ct);

        return rows.Select(x => new ListingResult(x.Id, x.EventId, x.TierId, VndAmount.FromDatabase(x.Price))).ToList();
    }

    public async Task RecoverPending(CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var rows = await _db.Records.AsNoTracking()
            .Where(x => x.Id.StartsWith("op:"))
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            var op = JsonSerializer.Deserialize<CoreOperation>(row.Json)!;
            if (op.State != "Pending" || op.NextAttemptAt > now)
            {
                continue;
            }

            var s = await Owned(op.Seller, op.SessionId, ct);
            if (s.PendingOperationId != row.Id)
            {
                continue;
            }

            await Transaction(async () =>
            {
                var current = (await _db.Read<CoreOperation>(row.Id, ct))!;
                if (current.State != "Pending")
                {
                    return false;
                }

                current.Attempts++;
                current.NextAttemptAt = now.AddSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(current.Attempts, 6))));
                await _db.Put(row.Id, current, ct);
                return true;
            }, ct, row.Id);

            try
            {
                await Recover(s, op, ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Reconciliation logs retained in durable state without leaking secrets
            }
        }
    }

    public sealed class MarketRow
    {
        public Guid Id { get; set; }
        public Guid EventId { get; set; }
        public Guid TierId { get; set; }
        public decimal Price { get; set; }
    }
}
