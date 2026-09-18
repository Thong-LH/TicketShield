using Grpc.Core;
using TicketShield.Contracts.Organizer.V1;
using Xunit;

namespace TicketShield.Resale.Tests;

public sealed class OrganizerTransferOwnershipTests(ResaleFixture f) : IClassFixture<ResaleFixture>
{
    [Fact]
    public async Task Transfer_ownership_changes_old_ticket_to_TRANSFERRED_and_issues_new_VALID_ticket()
    {
        // 1. Lock a ticket via OTP verification
        var ticketCode = await f.Ticket();
        var (verificationId, otp) = await f.Start(ticketCode);
        var confirmResult = await f.Post($"api/ticket-verifications/{verificationId}/confirm", new { otp });
        Assert.Equal(System.Net.HttpStatusCode.OK, confirmResult.Status);

        // Get lock details via gRPC
        var verificationView = await f.Grpc.GetVerificationAsync(
            new GetVerificationRequest { Verification = new VerificationReference { VerificationId = verificationId, RequesterRef = ResaleFixture.Seller } },
            f.Headers);
        var currentLock = verificationView.CurrentLock;
        Assert.NotNull(currentLock);

        // 2. Execute TransferOwnership RPC call
        var buyerRef = ResaleFixture.Id();
        var buyerEmail = "buyer.test@ticketshield.vn";
        var buyerName = "Nguyen Van Buyer";

        var transferRequest = new TransferOwnershipRequest
        {
            Operation = new OperationContext
            {
                OperationId = ResaleFixture.Id(),
                VerificationId = verificationId,
                RequesterRef = ResaleFixture.Seller
            },
            LockId = currentLock.ResaleLock.LockId,
            ExpectedLockGeneration = currentLock.ResaleLock.Generation,
            NewOwnerRef = buyerRef,
            NewOwnerEmail = buyerEmail,
            NewOwnerName = buyerName,
            NewOwnerPhone = "0988888888"
        };

        var response = await f.Grpc.TransferOwnershipAsync(transferRequest, f.Headers);

        // 3. Assertions
        Assert.Equal(TransferOutcome.Transferred, response.Outcome);
        Assert.Equal(ticketCode, response.OldTicket.TicketCode);
        Assert.NotNull(response.NewTicket);
        Assert.NotEqual(ticketCode, response.NewTicket.Ticket.TicketCode);

        // Verify old ticket status is TRANSFERRED in database
        var oldStatus = await f.Sql(f.Database.MockConnection, "SELECT status FROM mock_tickets WHERE ticket_code=@code", ("code", ticketCode));
        Assert.Equal("TRANSFERRED", oldStatus);

        // Verify new ticket is created with VALID status and buyer identity in database
        var newStatus = await f.Sql(f.Database.MockConnection, "SELECT status FROM mock_tickets WHERE ticket_code=@code", ("code", response.NewTicket.Ticket.TicketCode));
        Assert.Equal("VALID", newStatus);

        var newOwnerEmail = await f.Sql(f.Database.MockConnection, "SELECT owner_email FROM mock_tickets WHERE ticket_code=@code", ("code", response.NewTicket.Ticket.TicketCode));
        Assert.Equal(buyerEmail, newOwnerEmail);
    }

    [Fact]
    public async Task Transfer_ownership_is_idempotent_when_called_again_with_same_operation_id()
    {
        var ticketCode = await f.Ticket();
        var (verificationId, otp) = await f.Start(ticketCode);
        await f.Post($"api/ticket-verifications/{verificationId}/confirm", new { otp });

        var verificationView = await f.Grpc.GetVerificationAsync(
            new GetVerificationRequest { Verification = new VerificationReference { VerificationId = verificationId, RequesterRef = ResaleFixture.Seller } },
            f.Headers);

        var transferRequest = new TransferOwnershipRequest
        {
            Operation = new OperationContext
            {
                OperationId = ResaleFixture.Id(),
                VerificationId = verificationId,
                RequesterRef = ResaleFixture.Seller
            },
            LockId = verificationView.CurrentLock.ResaleLock.LockId,
            ExpectedLockGeneration = verificationView.CurrentLock.ResaleLock.Generation,
            NewOwnerRef = ResaleFixture.Id(),
            NewOwnerEmail = "buyer.idempotent@ticketshield.vn",
            NewOwnerName = "Idempotent Buyer"
        };

        var call1 = await f.Grpc.TransferOwnershipAsync(transferRequest, f.Headers);
        var call2 = await f.Grpc.TransferOwnershipAsync(transferRequest, f.Headers);

        Assert.Equal(call1.Outcome, call2.Outcome);
        Assert.Equal(call1.NewTicket.Ticket.TicketCode, call2.NewTicket.Ticket.TicketCode);
    }
}
