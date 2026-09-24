using System.Net;
using System.Net.Http.Json;
using TicketShield.Contracts.Organizer.V1;
using Xunit;

namespace TicketShield.Resale.Tests;

/// <summary>
/// BE-MOCK-4.3.3 — P2P: a VALID ticket issued to the buyer survives MockOrganizer
/// startup (reset-all runs the same seeder as process start). Non-VALID extras are still removed.
/// </summary>
public sealed class PreserveTransferredBuyerTicketsP2PTests(ResaleFixture f) : IClassFixture<ResaleFixture>
{
    [Fact]
    public async Task Restart_keeps_valid_buyer_ticket_and_drops_non_valid_extras()
    {
        var ticketCode = await f.Ticket();
        var (verificationId, otp) = await f.Start(ticketCode);
        var confirmed = await f.Post($"api/ticket-verifications/{verificationId}/confirm", new { otp });
        Assert.Equal(HttpStatusCode.OK, confirmed.Status);

        var verificationView = await f.Grpc.GetVerificationAsync(
            new GetVerificationRequest { Verification = new VerificationReference { VerificationId = verificationId, RequesterRef = ResaleFixture.Seller } },
            f.Headers);

        const string buyerEmail = "buyer.preserve@ticketshield.vn";
        var transfer = await f.Grpc.TransferOwnershipAsync(new TransferOwnershipRequest
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
            NewOwnerEmail = buyerEmail,
            NewOwnerName = "Preserved Buyer"
        }, f.Headers);
        Assert.Equal(TransferOutcome.Transferred, transfer.Outcome);
        var buyerCode = transfer.NewTicket.Ticket.TicketCode;

        var staleCode = await f.Ticket("USED");

        var reset = await f.MockHttp.PostAsync("api/organizer/reset-all", null);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var mine = await f.MockHttp.GetAsync($"api/v1/mock-tickets/my-tickets?email={buyerEmail}&status=VALID");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        var body = await mine.Content.ReadFromJsonAsync<BuyerTicketsQueryResult>();
        Assert.NotNull(body);
        Assert.Contains(body.Data, t => t.TicketCode == buyerCode && t.Status == "VALID" && t.OwnerEmail == buyerEmail);

        Assert.Equal(HttpStatusCode.NotFound, (await f.MockHttp.GetAsync($"api/v1/mock-tickets/{staleCode}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.MockHttp.GetAsync($"api/v1/mock-tickets/{ticketCode}")).StatusCode);

        var seed = await f.MockHttp.GetAsync("api/v1/mock-tickets/ATSH-VIP-888");
        Assert.Equal(HttpStatusCode.OK, seed.StatusCode);
    }

    private sealed class BuyerTicketsQueryResult
    {
        public List<BuyerTicketDto> Data { get; set; } = new();
    }

    private sealed class BuyerTicketDto
    {
        public string TicketCode { get; set; } = string.Empty;
        public string OwnerEmail { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
