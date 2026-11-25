using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MockOrganizer.API.Data;
using TicketShield.Contracts.Organizer.V1;
using Xunit;

namespace TicketShield.Resale.Tests;

/// <summary>
/// BE-MOCK-4.3.3 — P2P: a ticket issued to a resale buyer must survive a MockOrganizer restart.
/// Startup runs in "Keep" mode by default (Startup:DataMode=Keep): nothing that already exists is
/// deleted or overwritten. Only an explicit "Reset" (Startup:DataMode=Reset or reset-all) goes back
/// to the demo data set and drops the tickets outside it.
/// </summary>
public sealed class PreserveTransferredBuyerTicketsP2PTests(ResaleFixture f) : IClassFixture<ResaleFixture>
{
    [Fact]
    public async Task Restart_in_keep_mode_keeps_the_buyer_ticket_and_every_other_ticket()
    {
        var (originalCode, buyerCode, buyerEmail) = await TransferATicketToABuyerAsync();
        var staleCode = await f.Ticket("USED");

        await RestartMockSeederAsync(keepExistingData: true);

        var mine = await f.MockHttp.GetAsync($"api/v1/mock-tickets/my-tickets?email={buyerEmail}&status=VALID");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        var body = await mine.Content.ReadFromJsonAsync<BuyerTicketsQueryResult>();
        Assert.NotNull(body);
        Assert.Contains(body.Data, t => t.TicketCode == buyerCode && t.Status == "VALID" && t.OwnerEmail == buyerEmail);

        // Keep mode deletes nothing: the used extra ticket and the seller's old (transferred) ticket are still there.
        Assert.Equal(HttpStatusCode.OK, (await f.MockHttp.GetAsync($"api/v1/mock-tickets/{staleCode}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.MockHttp.GetAsync($"api/v1/mock-tickets/{originalCode}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.MockHttp.GetAsync("api/v1/mock-tickets/ATSH-VIP-888")).StatusCode);
    }

    [Fact]
    public async Task Reset_all_goes_back_to_the_demo_tickets_only()
    {
        var (originalCode, buyerCode, _) = await TransferATicketToABuyerAsync();

        var reset = await f.MockHttp.PostAsync("api/organizer/reset-all", null);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await f.MockHttp.GetAsync($"api/v1/mock-tickets/{buyerCode}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.MockHttp.GetAsync($"api/v1/mock-tickets/{originalCode}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.MockHttp.GetAsync("api/v1/mock-tickets/ATSH-VIP-888")).StatusCode);
    }

    /// Runs the real resale flow up to the organizer transfer and returns the old and the new ticket codes.
    private async Task<(string OriginalCode, string BuyerCode, string BuyerEmail)> TransferATicketToABuyerAsync()
    {
        var ticketCode = await f.Ticket();
        var (verificationId, otp) = await f.Start(ticketCode);
        var confirmed = await f.Post($"api/ticket-verifications/{verificationId}/confirm", new { otp });
        Assert.Equal(HttpStatusCode.OK, confirmed.Status);

        var verificationView = await f.Grpc.GetVerificationAsync(
            new GetVerificationRequest { Verification = new VerificationReference { VerificationId = verificationId, RequesterRef = ResaleFixture.Seller } },
            f.Headers);

        var buyerEmail = $"buyer.preserve.{ResaleFixture.Id()[..8]}@ticketshield.vn";
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
        return (ticketCode, transfer.NewTicket.Ticket.TicketCode, buyerEmail);
    }

    /// Runs the same seeding step MockOrganizer runs when the process starts.
    private async Task RestartMockSeederAsync(bool keepExistingData)
    {
        using var scope = f.Mock.Services.CreateScope();
        await OrganizerDatabaseSeeder.SeedOrganizerAsync(
            scope.ServiceProvider.GetRequiredService<OrganizerDbContext>(), keepExistingData: keepExistingData);
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
