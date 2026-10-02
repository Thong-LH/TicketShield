using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.ResaleListings.Commands.PublishResaleListing;
using TicketShield.Application.Resale;
using TicketShield.Domain.Exceptions;
using Xunit;

namespace TicketShield.UnitTests.Features.ResaleListings;

public class PublishResaleListingCommandHandlerTests
{
    private class FakeTicketVerificationService : ITicketVerificationService
    {
        public Task<VerificationResult> Publish(string seller, string key, PublishBody body, CancellationToken ct)
        {
            return Task.FromResult(new VerificationResult(
                body.VerificationId,
                "Published",
                null,
                null,
                null,
                null,
                2_000_000L,
                Guid.NewGuid()));
        }

        public Task CancelByListingId(string seller, Guid listingId, string key, CancellationToken ct) => Task.CompletedTask;
        public Task<VerificationResult> Request(string seller, string key, string ticket, CancellationToken ct) => throw new NotImplementedException();
        public Task<VerificationResult> Resend(string seller, string id, string key, CancellationToken ct) => throw new NotImplementedException();
        public Task<VerificationResult> Confirm(string seller, string id, string key, string otp, CancellationToken ct) => throw new NotImplementedException();
        public Task<VerificationResult> Get(string seller, string id, CancellationToken ct) => throw new NotImplementedException();
        public Task<VerificationResult> Close(string seller, string id, string key, CancellationToken ct) => throw new NotImplementedException();
        public Task<VerificationResult> Cancel(string seller, string id, string key, CancellationToken ct) => throw new NotImplementedException();
        public Task<List<ListingResult>> Marketplace(int page, int size, CancellationToken ct) => throw new NotImplementedException();
        public Task RecoverPending(CancellationToken ct) => throw new NotImplementedException();
        public Task<TicketShield.Contracts.Organizer.V1.TransferOwnershipResponse> TransferOwnership(string seller, string verificationId, string lockId, ulong expectedLockGeneration, string buyerRef, string buyerEmail, string buyerName, string? buyerPhone, CancellationToken ct) => throw new NotImplementedException();
        public Task<TicketShield.Contracts.Organizer.V1.TransferOwnershipResponse> TransferOwnershipByListingId(Guid listingId, Guid buyerId, string buyerEmail, string buyerName, string? buyerPhone, CancellationToken ct) => throw new NotImplementedException();
    }

    [Fact]
    public async Task Handle_WhenServiceNull_ThrowsResaleWorkflowException()
    {
        var handler = new PublishResaleListingCommandHandler(null);
        var command = new PublishResaleListingCommand("seller-1", "key-1", new PublishBody("v-1", 1_000_000));

        var ex = await Assert.ThrowsAsync<ResaleWorkflowException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Equal("SERVICE_UNAVAILABLE", ex.Code);
    }

    [Fact]
    public async Task Handle_WhenSellerEmpty_ThrowsUnauthorizedException()
    {
        var service = new FakeTicketVerificationService();
        var handler = new PublishResaleListingCommandHandler(service);
        var command = new PublishResaleListingCommand(string.Empty, "key-1", new PublishBody("v-1", 1_000_000));

        await Assert.ThrowsAsync<UnauthorizedException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenValid_ReturnsSuccessResponse()
    {
        var service = new FakeTicketVerificationService();
        var handler = new PublishResaleListingCommandHandler(service);
        var command = new PublishResaleListingCommand("seller-1", "key-1", new PublishBody("v-1", 1_000_000));

        var response = await handler.Handle(command, CancellationToken.None);

        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.Equal("Published", response.Data.Status);
    }
}
