using TicketShield.Application.Features.ResaleListings.Common;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using Xunit;

namespace TicketShield.UnitTests.Features.ResaleListings;

public class ResaleListingExtensionsTests
{
    [Fact]
    public void ToDetailDto_WhenListingTransactingWithTransferNeedsReviewEscrow_ShouldNotRevertToVerified()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            ListingStatus = ListingStatus.Transacting,
            Event = new Event
            {
                Id = Guid.NewGuid(),
                Name = "Rock Fest 2026",
                EventStartAt = now.AddDays(10)
            }
        };

        var reviewEscrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            Status = EscrowStatus.TransferNeedsReview,
            UnlockAt = null,
            CreatedAt = now.AddMinutes(-15)
        };
        listing.EscrowTransactions.Add(reviewEscrow);

        // Act
        var dto = listing.ToDetailDto();

        // Assert
        Assert.Equal("Transacting", dto.ListingStatus);
        Assert.Null(dto.UnlockAt);
    }

    [Fact]
    public void ToDetailDto_WhenListingTransactingWithLockedEscrow_ShouldNotRevertToVerified()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            ListingStatus = ListingStatus.Transacting,
            Event = new Event
            {
                Id = Guid.NewGuid(),
                Name = "EDM Party",
                EventStartAt = now.AddDays(5)
            }
        };

        var lockedEscrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            Status = EscrowStatus.Locked,
            UnlockAt = now.AddHours(24),
            CreatedAt = now.AddMinutes(-5)
        };
        listing.EscrowTransactions.Add(lockedEscrow);

        // Act
        var dto = listing.ToDetailDto();

        // Assert
        Assert.Equal("Transacting", dto.ListingStatus);
        Assert.Null(dto.UnlockAt);
    }

    [Fact]
    public void ToDetailDto_WhenListingTransactingAndHoldExpired_ShouldRevertToVerified()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            ListingStatus = ListingStatus.Transacting,
            Event = new Event
            {
                Id = Guid.NewGuid(),
                Name = "Acoustic Night",
                EventStartAt = now.AddDays(2)
            }
        };

        var expiredEscrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            Status = EscrowStatus.Pending,
            UnlockAt = now.AddMinutes(-5),
            CreatedAt = now.AddMinutes(-15)
        };
        listing.EscrowTransactions.Add(expiredEscrow);

        // Act
        var dto = listing.ToDetailDto();

        // Assert
        Assert.Equal("Verified", dto.ListingStatus);
        Assert.Null(dto.UnlockAt);
    }

    [Fact]
    public void ToDetailDto_WhenListingTransactingAndHoldExpiredAndPastCutoff_ShouldRevertToExpired()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            ListingStatus = ListingStatus.Transacting,
            Event = new Event
            {
                Id = Guid.NewGuid(),
                Name = "Past Concert",
                EventStartAt = now.AddMinutes(60) // within 2 hours cutoff
            }
        };

        var expiredEscrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            Status = EscrowStatus.Pending,
            UnlockAt = now.AddMinutes(-5),
            CreatedAt = now.AddMinutes(-15)
        };
        listing.EscrowTransactions.Add(expiredEscrow);

        // Act
        var dto = listing.ToDetailDto();

        // Assert
        Assert.Equal("Expired", dto.ListingStatus);
        Assert.Null(dto.UnlockAt);
    }
}
