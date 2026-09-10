using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MockOrganizer.API.Resale;
using TicketShield.Application.Common.Models;
using TicketShield.Contracts.Organizer.V1;
using TicketShield.Infrastructure.Resale;
using Xunit;

namespace TicketShield.Resale.Tests;

public sealed class ResaleFlowTests(ResaleFixture f) : IClassFixture<ResaleFixture>
{
    [Fact]
    public async Task Public_flow_uses_real_smtp_grpc_and_postgres_with_replay_and_cancel()
    {
        var ticket = await f.Ticket(); var requestKey = ResaleFixture.Id(); var before = f.Mail.Messages.Count;
        var requested = await f.Post("api/ticket-verifications", new { ticketCode = ticket }, requestKey);
        Assert.Equal(HttpStatusCode.OK, requested.Status);
        var id = requested.Body.GetProperty("data").GetProperty("verificationId").GetString()!;
        var otp = f.Mail.Messages.Last().Otp;
        Assert.Contains("seller@example.invalid", f.Mail.Messages.Last().Recipient);
        var replay = await f.Post("api/ticket-verifications", new { ticketCode = ticket }, requestKey);
        Assert.Equal(id, replay.Body.GetProperty("data").GetProperty("verificationId").GetString());
        Assert.Equal(before + 1, f.Mail.Messages.Count);
        var changed = await f.Post("api/ticket-verifications", new { ticketCode = "different" }, requestKey);
        Assert.Equal(HttpStatusCode.Conflict, changed.Status);
        var confirmKey = ResaleFixture.Id();
        var confirmed = await f.Post($"api/ticket-verifications/{id}/confirm", new { otp }, confirmKey);
        Assert.Equal(HttpStatusCode.OK, confirmed.Status);
        Assert.Equal(2500000L, confirmed.Body.GetProperty("data").GetProperty("originalPrice").GetInt64());
        Assert.Equal(HttpStatusCode.OK, (await f.Post($"api/ticket-verifications/{id}/confirm", new { otp }, confirmKey)).Status);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await f.Post("api/resale-listings", new { verificationId = id, resalePrice = 2500001 })).Status);
        var publishKey = ResaleFixture.Id();
        var publish = await f.Post("api/resale-listings", new { verificationId = id, resalePrice = 2500000 }, publishKey);
        Assert.Equal(HttpStatusCode.OK, publish.Status);
        var listing = publish.Body.GetProperty("data").GetProperty("listingId").GetGuid();
        Assert.Equal(listing, (await f.Post("api/resale-listings", new { verificationId = id, resalePrice = 2500000 }, publishKey)).Body.GetProperty("data").GetProperty("listingId").GetGuid());
        Assert.Equal(1L, await f.Sql(f.Database.CoreConnection, "SELECT count(*) FROM resale_listings WHERE original_ticket_code=@code", ("code", ticket)));
        var market = await f.Http.GetStringAsync("api/resale-listings"); Assert.Contains(listing.ToString(), market); Assert.DoesNotContain(ticket, market);
        var coreRows = (string)(await f.Sql(f.Database.CoreConnection, "SELECT string_agg(\"Json\"::text, '') FROM core_resale_records"))!;
        var mockRows = (string)(await f.Sql(f.Database.MockConnection, "SELECT string_agg(\"Json\"::text, '') FROM organizer_resale_records"))!;
        Assert.DoesNotContain("\"otp\":", coreRows, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"otp\":", mockRows, StringComparison.OrdinalIgnoreCase);
        var cancel = await f.Post($"api/ticket-verifications/{id}/cancel-listing", new { }); Assert.Equal(HttpStatusCode.OK, cancel.Status);
        Assert.Equal("VALID", await f.Sql(f.Database.MockConnection, "SELECT status FROM mock_tickets WHERE ticket_code=@code", ("code", ticket)));
        Assert.DoesNotContain(listing.ToString(), await f.Http.GetStringAsync("api/resale-listings"));
    }
    [Fact]
    public async Task Ownership_auth_and_publish_policies_are_enforced()
    {
        var ticket = await f.Ticket();
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Post("api/ticket-verifications", new { ticketCode = ticket }, seller: null)).Status);
        var (id, otp) = await f.Start(ticket);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Post($"api/ticket-verifications/{id}/confirm", new { otp }, seller: ResaleFixture.OtherSeller)).Status);
        Assert.Equal(HttpStatusCode.OK, (await f.Post($"api/ticket-verifications/{id}/confirm", new { otp })).Status);
        foreach (var price in new long[] { 0, -1, VndAmount.MaxDatabaseValue + 1 })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await f.Post("api/resale-listings", new { verificationId = id, resalePrice = price })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Post("api/resale-listings", new { verificationId = id, resalePrice = 10.5 })).Status);
        var privateRes = await f.Post("api/resale-listings", new { verificationId = id, resalePrice = 100, isPrivate = true });
        Assert.Equal(HttpStatusCode.OK, privateRes.Status);
        Assert.False(string.IsNullOrEmpty(privateRes.Body.GetProperty("data").GetProperty("privateAccessToken").GetString()));
        using var unauth = f.Grpc.RequestTicketOtpAsync(new RequestTicketOtpRequest());
        Assert.Equal(StatusCode.Unauthenticated, (await Assert.ThrowsAsync<RpcException>(async () => await unauth.ResponseAsync)).StatusCode);
    }
    [Fact]
    public async Task Wrong_otp_counter_is_durable_and_same_operation_counts_once()
    {
        var (id, otp) = await f.Start(); var key = ResaleFixture.Id(); var wrong = otp == "000000" ? "111111" : "000000";
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.Conflict, (await f.Post($"api/ticket-verifications/{id}/confirm", new { otp = wrong }, key)).Status);
        Assert.Equal(1L, await f.Sql(f.Database.MockConnection, "SELECT (\"Json\"->>'Failures')::bigint FROM organizer_resale_records WHERE \"Id\"=@id", ("id", "session:" + id)));
        for (var i = 0; i < 4; i++) Assert.Equal(HttpStatusCode.Conflict, (await f.Post($"api/ticket-verifications/{id}/confirm", new { otp = wrong })).Status);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await f.Post($"api/ticket-verifications/{id}/confirm", new { otp })).Status);
        Assert.Equal(5L, await f.Sql(f.Database.MockConnection, "SELECT (\"Json\"->>'Failures')::bigint FROM organizer_resale_records WHERE \"Id\"=@id", ("id", "session:" + id)));
    }
    [Fact]
    public async Task Resend_supersedes_code_and_expiry_is_checked()
    {
        var (id, oldOtp) = await f.Start();
        Assert.Equal(HttpStatusCode.TooManyRequests, (await f.Post($"api/ticket-verifications/{id}/resend", new { })).Status);
        f.Clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(HttpStatusCode.OK, (await f.Post($"api/ticket-verifications/{id}/resend", new { })).Status);
        var newOtp = f.Mail.Messages.Last().Otp;
        // Generation binding is definitive even if a random OTP coincidentally repeats.
        var view = await f.Grpc.GetVerificationAsync(new GetVerificationRequest { Verification = new() { VerificationId = id, RequesterRef = ResaleFixture.Seller } }, f.Headers);
        Assert.Equal(2U, view.Challenge.Generation);
        if (newOtp != oldOtp) Assert.Equal(HttpStatusCode.Conflict, (await f.Post($"api/ticket-verifications/{id}/confirm", new { otp = oldOtp })).Status);
        f.Clock.Advance(TimeSpan.FromSeconds(301));
        var expired = await f.Post($"api/ticket-verifications/{id}/confirm", new { otp = newOtp });
        Assert.Equal(HttpStatusCode.Conflict, expired.Status); Assert.Equal("OTP_EXPIRED", expired.Body.GetProperty("message").GetString());
    }
    [Theory]
    [InlineData("USED")]
    [InlineData("CANCELLED")]
    public async Task Invalid_ticket_is_rejected_before_email(string status)
    {
        var ticket = await f.Ticket(status); var before = f.Mail.Messages.Count;
        Assert.Equal(HttpStatusCode.Conflict, (await f.Post("api/ticket-verifications", new { ticketCode = ticket })).Status);
        Assert.Equal(before, f.Mail.Messages.Count);
    }
    [Fact]
    public async Task Owner_change_after_otp_prevents_lock()
    {
        var ticket = await f.Ticket(); var (id, otp) = await f.Start(ticket);
        await f.Sql(f.Database.MockConnection, "UPDATE mock_tickets SET owner_email='new-owner@example.invalid' WHERE ticket_code=@code", ("code", ticket));
        Assert.Equal(HttpStatusCode.Conflict, (await f.Post($"api/ticket-verifications/{id}/confirm", new { otp })).Status);
        Assert.Equal("VALID", await f.Sql(f.Database.MockConnection, "SELECT status FROM mock_tickets WHERE ticket_code=@code", ("code", ticket)));
    }
    [Fact]
    public async Task Two_sessions_for_one_ticket_can_acquire_only_one_lock()
    {
        var ticket = await f.Ticket(); var a = await f.Start(ticket); var b = await f.Start(ticket);
        var responses = await Task.WhenAll(f.Post($"api/ticket-verifications/{a.Verification}/confirm", new { otp = a.Otp }),
            f.Post($"api/ticket-verifications/{b.Verification}/confirm", new { otp = b.Otp }));
        Assert.Single(responses, x => x.Status == HttpStatusCode.OK); Assert.Single(responses, x => x.Status == HttpStatusCode.Conflict);
        Assert.Equal(1L, await f.Sql(f.Database.MockConnection, "SELECT count(*) FROM organizer_resale_records WHERE \"Id\" LIKE 'lock:%' AND \"Json\"->>'TicketCode'=@code AND \"Json\"->>'ReleasedAt' IS NULL", ("code", ticket)));
    }
    [Fact]
    public async Task Close_fences_a_late_request_and_stale_release_cannot_unlock_new_generation()
    {
        var lateId = ResaleFixture.Id(); var ticket = await f.Ticket();
        var op = new OperationContext { OperationId = ResaleFixture.Id(), VerificationId = lateId, RequesterRef = ResaleFixture.Seller };
        await f.Grpc.CloseVerificationAsync(new CloseVerificationRequest { Operation = op }, f.Headers);
        op.OperationId = ResaleFixture.Id();
        using var late = f.Grpc.RequestTicketOtpAsync(new RequestTicketOtpRequest { Operation = op, Ticket = new() { OrganizerId = ResaleFixture.Organizer, TicketCode = ticket } }, f.Headers);
        await Assert.ThrowsAsync<RpcException>(async () => await late.ResponseAsync);
        var a = await f.Start(ticket); await f.Post($"api/ticket-verifications/{a.Verification}/confirm", new { otp = a.Otp });
        var old = await f.Grpc.GetVerificationAsync(new GetVerificationRequest { Verification = new() { VerificationId = a.Verification, RequesterRef = ResaleFixture.Seller } }, f.Headers);
        await f.Post($"api/ticket-verifications/{a.Verification}/close", new { });
        var b = await f.Start(ticket); await f.Post($"api/ticket-verifications/{b.Verification}/confirm", new { otp = b.Otp });
        var released = await f.Grpc.ReleaseResaleLockAsync(new ReleaseResaleLockRequest { Operation = new() { OperationId = ResaleFixture.Id(), VerificationId = a.Verification, RequesterRef = ResaleFixture.Seller }, LockId = old.CurrentLock.ResaleLock.LockId, ExpectedLockGeneration = old.CurrentLock.ResaleLock.Generation, Reason = ReleaseReason.VerificationAbandoned }, f.Headers);
        Assert.Equal(ReleaseOutcome.AlreadyReleased, released.Outcome);
        Assert.Equal("LOCKED_FOR_RESALE", await f.Sql(f.Database.MockConnection, "SELECT status FROM mock_tickets WHERE ticket_code=@code", ("code", ticket)));
    }
    [Fact]
    public async Task Smtp_rejection_is_reported_as_failed_not_delivered()
    {
        f.Mail.Reject = true;
        try {
            var result = await f.Post("api/ticket-verifications", new { ticketCode = await f.Ticket() });
            Assert.Equal(HttpStatusCode.OK, result.Status);
            Assert.Equal("Failed", result.Body.GetProperty("data").GetProperty("deliveryState").GetString());
        } finally { f.Mail.Reject = false; }
    }
    [Fact]
    public async Task Core_recovers_an_organizer_commit_after_the_response_is_lost()
    {
        var ticket = await f.Ticket();
        var verification = ResaleFixture.Id();
        var operation = ResaleFixture.Id();
        using (var scope = f.Core.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<CoreResaleStore>();
            var operationKey = $"op:{ResaleFixture.Seller}:Request:{operation}";
            await store.Put("session:" + verification, new CoreSession
            {
                Id = verification,
                Seller = ResaleFixture.Seller,
                TicketCode = ticket,
                State = "RequestPending",
                PendingOperationId = operationKey,
                UpdatedAt = f.Clock.GetUtcNow()
            }, default);
            await store.Put(operationKey, new CoreOperation
            {
                Id = operation,
                SessionId = verification,
                Seller = ResaleFixture.Seller,
                Kind = "Request",
                Fingerprint = "simulated-lost-response",
                State = "Pending"
            }, default);
            await store.SaveChangesAsync();
        }
        await f.Grpc.RequestTicketOtpAsync(new RequestTicketOtpRequest
        {
            Operation = new OperationContext { OperationId = operation, VerificationId = verification, RequesterRef = ResaleFixture.Seller },
            Ticket = new TicketReference { OrganizerId = ResaleFixture.Organizer, TicketCode = ticket }
        }, f.Headers);
        var recovered = await f.Get($"api/ticket-verifications/{verification}");
        Assert.Equal(HttpStatusCode.OK, recovered.Status);
        Assert.Equal("OtpReady", recovered.Body.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(1L, await f.Sql(f.Database.MockConnection,
            "SELECT count(*) FROM organizer_resale_records WHERE \"Id\"=@id",
            ("id", $"op:ticketshield-core:RequestOtp:{operation}")));
    }
    [Theory]
    [InlineData("0", 0L)]
    [InlineData("2500000.00", 2500000L)]
    [InlineData("9999999999999", 9999999999999L)]
    public void Vnd_conversion_is_exact(string value, long expected) => Assert.Equal(expected, VndAmount.FromDatabase(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
    [Theory]
    [InlineData("0.01")]
    [InlineData("-1")]
    [InlineData("10000000000000")]
    public void Vnd_conversion_never_silently_rounds(string value) => Assert.Throws<ArgumentOutOfRangeException>(() => VndAmount.FromDatabase(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
}
