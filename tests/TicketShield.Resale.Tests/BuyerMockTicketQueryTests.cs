using System.Net;
using System.Net.Http.Json;
using TicketShield.Contracts.Organizer.V1;
using Xunit;

namespace TicketShield.Resale.Tests;

public sealed class BuyerMockTicketQueryTests(ResaleFixture f) : IClassFixture<ResaleFixture>
{
    [Fact]
    public async Task Get_my_tickets_returns_newly_transferred_ticket_for_buyer()
    {
        // 1. Setup ticket and complete transfer to buyer
        var ticketCode = await f.Ticket();
        var (verificationId, otp) = await f.Start(ticketCode);
        await f.Post($"api/ticket-verifications/{verificationId}/confirm", new { otp });

        var verificationView = await f.Grpc.GetVerificationAsync(
            new GetVerificationRequest { Verification = new VerificationReference { VerificationId = verificationId, RequesterRef = ResaleFixture.Seller } },
            f.Headers);

        var buyerEmail = "buyer.query.test@ticketshield.vn";
        var buyerName = "Buyer Query User";

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
            NewOwnerEmail = buyerEmail,
            NewOwnerName = buyerName
        };

        var transferResponse = await f.Grpc.TransferOwnershipAsync(transferRequest, f.Headers);
        Assert.Equal(TransferOutcome.Transferred, transferResponse.Outcome);

        // 2. Query buyer tickets via GET /api/v1/mock-tickets/my-tickets?email=...
        var httpResponse = await f.MockHttp.GetAsync($"api/v1/mock-tickets/my-tickets?email={buyerEmail}");

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);

        var result = await httpResponse.Content.ReadFromJsonAsync<BuyerTicketsQueryResult>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.True(result.Count > 0);
        Assert.Contains(result.Data, t => t.TicketCode == transferResponse.NewTicket.Ticket.TicketCode && t.Status == "VALID");
    }

    private sealed class BuyerTicketsQueryResult
    {
        public bool Success { get; set; }
        public int Count { get; set; }
        public List<BuyerTicketDto> Data { get; set; } = new();
    }

    private sealed class BuyerTicketDto
    {
        public string TicketCode { get; set; } = string.Empty;
        public string EventName { get; set; } = string.Empty;
        public string SeatZone { get; set; } = string.Empty;
        public decimal OriginalPrice { get; set; }
        public string OwnerEmail { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
