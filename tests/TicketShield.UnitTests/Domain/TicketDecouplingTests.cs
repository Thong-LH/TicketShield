using Microsoft.EntityFrameworkCore;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using Xunit;

namespace TicketShield.UnitTests.Domain;

public class TicketDecouplingTests
{
    [Fact]
    public void Ticket_CreationAndDecoupling_ShouldRetainIdentityAndProperties()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var tierId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();

        var ticket = new Ticket
        {
            Id = ticketId,
            EventId = eventId,
            TierId = tierId,
            CurrentOwnerId = ownerId,
            TicketCode = "TCK-100-XYZ",
            SeatZone = "VIP-A1",
            OriginalPrice = 1_500_000m,
            Status = TicketStatus.Valid
        };

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Ticket = ticket,
            EventId = eventId,
            TierId = tierId,
            SellerId = ownerId,
            OriginalTicketCode = ticket.TicketCode,
            OriginalPrice = ticket.OriginalPrice,
            ResalePrice = 1_200_000m,
            SeatZone = ticket.SeatZone,
            ListingStatus = ListingStatus.Verified
        };

        // Assert
        Assert.Equal(ticketId, listing.TicketId);
        Assert.NotNull(listing.Ticket);
        Assert.Equal("TCK-100-XYZ", listing.Ticket.TicketCode);
        Assert.Equal(TicketStatus.Valid, listing.Ticket.Status);
        Assert.Equal(ownerId, listing.Ticket.CurrentOwnerId);
    }

    [Fact]
    public void Ticket_StatusTransitions_ShouldReflectLifecycle()
    {
        // Arrange
        var ticket = new Ticket
        {
            TicketCode = "TCK-LIFECYCLE-1",
            Status = TicketStatus.Valid
        };

        // Act & Assert 1: Đưa lên sàn niêm yết
        ticket.Status = TicketStatus.LockedForResale;
        Assert.Equal(TicketStatus.LockedForResale, ticket.Status);

        // Act & Assert 2: Chuyển quyền sở hữu cho Buyer
        ticket.Status = TicketStatus.Transferred;
        Assert.Equal(TicketStatus.Transferred, ticket.Status);

        // Act & Assert 3: Soát vé qua cổng
        ticket.Status = TicketStatus.Used;
        Assert.Equal(TicketStatus.Used, ticket.Status);

        // Act & Assert 4: Khiếu nại được duyệt hoàn tiền -> Hủy vé
        ticket.Status = TicketStatus.Cancelled;
        Assert.Equal(TicketStatus.Cancelled, ticket.Status);
    }
}
