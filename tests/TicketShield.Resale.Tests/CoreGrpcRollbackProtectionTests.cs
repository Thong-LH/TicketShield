using System.Net;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Contracts.Organizer.V1;
using Xunit;

namespace TicketShield.Resale.Tests;

public sealed class CoreGrpcRollbackProtectionTests(ResaleFixture f) : IClassFixture<ResaleFixture>
{
    [Fact]
    public async Task TransferOwnership_successful_grpc_call_commits_db_transaction()
    {
        // 1. Seller locks ticket
        var ticketCode = await f.Ticket();
        var (verificationId, otp) = await f.Start(ticketCode);
        await f.Post($"api/ticket-verifications/{verificationId}/confirm", new { otp });

        // Get lock details
        var verificationView = await f.Grpc.GetVerificationAsync(
            new GetVerificationRequest { Verification = new VerificationReference { VerificationId = verificationId, RequesterRef = ResaleFixture.Seller } },
            f.Headers);

        using var scope = f.Core.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITicketVerificationService>();

        var buyerRef = ResaleFixture.Id();
        var buyerEmail = "buyer.rollback.success@ticketshield.vn";
        var buyerName = "Buyer Rollback Success";

        // 2. Call TransferOwnership via ITicketVerificationService
        var response = await service.TransferOwnership(
            seller: ResaleFixture.Seller,
            verificationId: verificationId,
            lockId: verificationView.CurrentLock.ResaleLock.LockId,
            expectedLockGeneration: verificationView.CurrentLock.ResaleLock.Generation,
            buyerRef: buyerRef,
            buyerEmail: buyerEmail,
            buyerName: buyerName,
            buyerPhone: "0911223344",
            ct: CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(TransferOutcome.Transferred, response.Outcome);

        // 3. Verify session state in Core DB is updated to Transferred
        var updatedView = await service.Get(ResaleFixture.Seller, verificationId, CancellationToken.None);
        Assert.Equal("Transferred", updatedView.Status);
    }

    [Fact]
    public async Task TransferOwnership_failed_grpc_call_rolls_back_db_transaction_100_percent()
    {
        // 1. Seller locks ticket
        var ticketCode = await f.Ticket();
        var (verificationId, otp) = await f.Start(ticketCode);
        await f.Post($"api/ticket-verifications/{verificationId}/confirm", new { otp });

        using var scope = f.Core.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITicketVerificationService>();

        var invalidLockId = Guid.NewGuid().ToString("D"); // Non-existent lock ID causes gRPC call to fail

        // 2. Attempt TransferOwnership with invalid lock ID -> expect gRPC exception
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await service.TransferOwnership(
                seller: ResaleFixture.Seller,
                verificationId: verificationId,
                lockId: invalidLockId,
                expectedLockGeneration: 999,
                buyerRef: ResaleFixture.Id(),
                buyerEmail: "buyer.failed@ticketshield.vn",
                buyerName: "Failed Buyer",
                buyerPhone: null,
                ct: CancellationToken.None);
        });

        // 3. Rollback Verification: Core session status MUST remain unchanged (Verified / Locked), NOT updated to Transferred
        var sessionAfterFailure = await service.Get(ResaleFixture.Seller, verificationId, CancellationToken.None);
        Assert.NotEqual("Transferred", sessionAfterFailure.Status);
        Assert.Equal("Verified", sessionAfterFailure.Status);
    }
}
