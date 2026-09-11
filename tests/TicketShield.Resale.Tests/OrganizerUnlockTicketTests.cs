using Grpc.Core;
using TicketShield.Contracts.Organizer.V1;
using Xunit;

namespace TicketShield.Resale.Tests;

public sealed class OrganizerUnlockTicketTests(ResaleFixture f) : IClassFixture<ResaleFixture>
{
    [Fact]
    public async Task Unlock_original_ticket_from_locked_for_resale_to_valid_succeeds()
    {
        // 1. Create a ticket and complete lock flow
        var ticket = await f.Ticket();
        var (verificationId, otp) = await f.Start(ticket);
        var confirmResult = await f.Post($"api/ticket-verifications/{verificationId}/confirm", new { otp });
        Assert.Equal(System.Net.HttpStatusCode.OK, confirmResult.Status);

        // Verify ticket status is LOCKED_FOR_RESALE in database
        Assert.Equal("LOCKED_FOR_RESALE", await f.Sql(f.Database.MockConnection, "SELECT status FROM mock_tickets WHERE ticket_code=@code", ("code", ticket)));

        // Get lock details via gRPC
        var verificationView = await f.Grpc.GetVerificationAsync(
            new GetVerificationRequest { Verification = new VerificationReference { VerificationId = verificationId, RequesterRef = ResaleFixture.Seller } },
            f.Headers);
        var currentLock = verificationView.CurrentLock;
        Assert.NotNull(currentLock);

        // 2. Call ReleaseResaleLock gRPC endpoint (SCRUM-34 requirement)
        var releaseRequest = new ReleaseResaleLockRequest
        {
            Operation = new OperationContext
            {
                OperationId = ResaleFixture.Id(),
                VerificationId = verificationId,
                RequesterRef = ResaleFixture.Seller
            },
            LockId = currentLock.ResaleLock.LockId,
            ExpectedLockGeneration = currentLock.ResaleLock.Generation,
            Reason = ReleaseReason.ListingCancelled
        };

        var releaseResponse = await f.Grpc.ReleaseResaleLockAsync(releaseRequest, f.Headers);

        // 3. Verify response and status transition back to VALID
        Assert.Equal(ReleaseOutcome.Released, releaseResponse.Outcome);
        Assert.NotNull(releaseResponse.ResaleLock.ReleasedAt);
        Assert.Equal("VALID", await f.Sql(f.Database.MockConnection, "SELECT status FROM mock_tickets WHERE ticket_code=@code", ("code", ticket)));
    }

    [Fact]
    public async Task Unlock_original_ticket_is_idempotent_when_called_again()
    {
        // 1. Lock and then unlock ticket once
        var ticket = await f.Ticket();
        var (verificationId, otp) = await f.Start(ticket);
        await f.Post($"api/ticket-verifications/{verificationId}/confirm", new { otp });

        var verificationView = await f.Grpc.GetVerificationAsync(
            new GetVerificationRequest { Verification = new VerificationReference { VerificationId = verificationId, RequesterRef = ResaleFixture.Seller } },
            f.Headers);
        var currentLock = verificationView.CurrentLock;

        var releaseRequest = new ReleaseResaleLockRequest
        {
            Operation = new OperationContext
            {
                OperationId = ResaleFixture.Id(),
                VerificationId = verificationId,
                RequesterRef = ResaleFixture.Seller
            },
            LockId = currentLock.ResaleLock.LockId,
            ExpectedLockGeneration = currentLock.ResaleLock.Generation,
            Reason = ReleaseReason.ListingCancelled
        };

        var firstCall = await f.Grpc.ReleaseResaleLockAsync(releaseRequest, f.Headers);
        Assert.Equal(ReleaseOutcome.Released, firstCall.Outcome);

        // 2a. Replaying the exact same operation returns cached result (Outcome = Released)
        var replayCall = await f.Grpc.ReleaseResaleLockAsync(releaseRequest, f.Headers);
        Assert.Equal(ReleaseOutcome.Released, replayCall.Outcome);

        // 2b. Calling with a new operation ID on an already released lock returns Outcome = AlreadyReleased
        var newOpRequest = new ReleaseResaleLockRequest
        {
            Operation = new OperationContext
            {
                OperationId = ResaleFixture.Id(),
                VerificationId = verificationId,
                RequesterRef = ResaleFixture.Seller
            },
            LockId = currentLock.ResaleLock.LockId,
            ExpectedLockGeneration = currentLock.ResaleLock.Generation,
            Reason = ReleaseReason.ListingCancelled
        };
        var secondCall = await f.Grpc.ReleaseResaleLockAsync(newOpRequest, f.Headers);
        Assert.Equal(ReleaseOutcome.AlreadyReleased, secondCall.Outcome);
        Assert.Equal("VALID", await f.Sql(f.Database.MockConnection, "SELECT status FROM mock_tickets WHERE ticket_code=@code", ("code", ticket)));
    }

    [Fact]
    public async Task Unlock_original_ticket_rejects_invalid_release_reason()
    {
        // 1. Lock ticket
        var ticket = await f.Ticket();
        var (verificationId, otp) = await f.Start(ticket);
        await f.Post($"api/ticket-verifications/{verificationId}/confirm", new { otp });

        var verificationView = await f.Grpc.GetVerificationAsync(
            new GetVerificationRequest { Verification = new VerificationReference { VerificationId = verificationId, RequesterRef = ResaleFixture.Seller } },
            f.Headers);
        var currentLock = verificationView.CurrentLock;

        // 2. Attempt unlock with Unspecified release reason
        var invalidRequest = new ReleaseResaleLockRequest
        {
            Operation = new OperationContext
            {
                OperationId = ResaleFixture.Id(),
                VerificationId = verificationId,
                RequesterRef = ResaleFixture.Seller
            },
            LockId = currentLock.ResaleLock.LockId,
            ExpectedLockGeneration = currentLock.ResaleLock.Generation,
            Reason = ReleaseReason.Unspecified
        };

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await f.Grpc.ReleaseResaleLockAsync(invalidRequest, f.Headers));

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        Assert.Equal("LOCKED_FOR_RESALE", await f.Sql(f.Database.MockConnection, "SELECT status FROM mock_tickets WHERE ticket_code=@code", ("code", ticket)));
    }
}
