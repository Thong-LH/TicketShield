using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Workers;
using Xunit;

namespace TicketShield.UnitTests.Workers;

public class ExpiredHoldReleaseWorkerTests
{
    private static (TicketShieldDbContext dbContext, ServiceProvider serviceProvider) CreateInMemoryDbContextWithServices()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();

        services.AddDbContext<TicketShieldDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: dbName));

        services.AddScoped<ITicketShieldDbContext>(provider =>
            provider.GetRequiredService<TicketShieldDbContext>());

        var provider = services.BuildServiceProvider();
        var dbContext = provider.GetRequiredService<TicketShieldDbContext>();

        var organizer = new Organizer
        {
            Id = Guid.NewGuid(),
            Name = "V-Concert Organizer",
            OfficialEmail = "vconcert@test.com"
        };
        var testEvent = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizer.Id,
            Name = "V-Concert Hanoi 2026",
            Venue = "Trung tam Hoi nghi Quoc gia",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(15),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(15).AddHours(3),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(14),
            Organizer = organizer
        };
        var tier = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierName = "VIP",
            OriginalPrice = 2_000_000m,
            Event = testEvent
        };
        var seller = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "seller@ticketshield.vn",
            FullName = "Nguyen Van Seller"
        };
        var buyer = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "buyer@ticketshield.vn",
            FullName = "Tran Van Buyer"
        };

        dbContext.Organizers.Add(organizer);
        dbContext.Events.Add(testEvent);
        dbContext.TicketTiers.Add(tier);
        dbContext.ShadowUsers.AddRange(seller, buyer);
        dbContext.SaveChanges();

        return (dbContext, provider);
    }

    [Fact]
    public async Task ReleaseExpiredHolds_WhenListingHoldIsExpired_ShouldRevertStatusToVerified()
    {
        // Arrange
        var (dbContext, serviceProvider) = CreateInMemoryDbContextWithServices();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = (await dbContext.Events.FirstAsync()).Id,
            TierId = (await dbContext.TicketTiers.FirstAsync()).Id,
            SellerId = (await dbContext.ShadowUsers.FirstAsync()).Id,
            OriginalTicketCode = "TCK-EXPIRED-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Transacting
        };
        var expiredEscrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = (await dbContext.ShadowUsers.Skip(1).FirstAsync()).Id,
            SellerId = listing.SellerId,
            PaymentReference = "TSEXPIRED01",
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-1) // Expired 1 minute ago
        };
        listing.EscrowTransaction = expiredEscrow;
        dbContext.ResaleListings.Add(listing);
        dbContext.EscrowTransactions.Add(expiredEscrow);
        await dbContext.SaveChangesAsync();

        var worker = new ExpiredHoldReleaseWorker(scopeFactory, NullLogger<ExpiredHoldReleaseWorker>.Instance);

        // Act
        var releasedCount = await worker.ReleaseExpiredHoldsAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, releasedCount);
        await dbContext.Entry(listing).ReloadAsync();
        Assert.Equal(ListingStatus.Verified, listing.ListingStatus);
    }

    [Fact]
    public async Task ReleaseExpiredHolds_WhenListingHoldIsActive_ShouldNotRevertStatus()
    {
        // Arrange
        var (dbContext, serviceProvider) = CreateInMemoryDbContextWithServices();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = (await dbContext.Events.FirstAsync()).Id,
            TierId = (await dbContext.TicketTiers.FirstAsync()).Id,
            SellerId = (await dbContext.ShadowUsers.FirstAsync()).Id,
            OriginalTicketCode = "TCK-ACTIVE-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Transacting
        };
        var activeEscrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = (await dbContext.ShadowUsers.Skip(1).FirstAsync()).Id,
            SellerId = listing.SellerId,
            PaymentReference = "TSACTIVE01",
            Status = EscrowStatus.Pending,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(5) // Still active
        };
        listing.EscrowTransaction = activeEscrow;
        dbContext.ResaleListings.Add(listing);
        dbContext.EscrowTransactions.Add(activeEscrow);
        await dbContext.SaveChangesAsync();

        var worker = new ExpiredHoldReleaseWorker(scopeFactory, NullLogger<ExpiredHoldReleaseWorker>.Instance);

        // Act
        var releasedCount = await worker.ReleaseExpiredHoldsAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, releasedCount);
        var updatedListing = await dbContext.ResaleListings.FirstAsync(l => l.Id == listing.Id);
        Assert.Equal(ListingStatus.Transacting, updatedListing.ListingStatus);
    }

    [Fact]
    public async Task ReleaseExpiredHolds_WhenListingIsSoldOrCancelled_ShouldNotChangeStatus()
    {
        // Arrange
        var (dbContext, serviceProvider) = CreateInMemoryDbContextWithServices();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var listingSold = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = (await dbContext.Events.FirstAsync()).Id,
            TierId = (await dbContext.TicketTiers.FirstAsync()).Id,
            SellerId = (await dbContext.ShadowUsers.FirstAsync()).Id,
            OriginalTicketCode = "TCK-SOLD-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Sold
        };
        var escrowSold = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listingSold.Id,
            BuyerId = Guid.NewGuid(),
            SellerId = listingSold.SellerId,
            PaymentReference = "TSSOLD01",
            Status = EscrowStatus.Locked,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        listingSold.EscrowTransaction = escrowSold;
        dbContext.ResaleListings.Add(listingSold);
        dbContext.EscrowTransactions.Add(escrowSold);
        await dbContext.SaveChangesAsync();

        var worker = new ExpiredHoldReleaseWorker(scopeFactory, NullLogger<ExpiredHoldReleaseWorker>.Instance);

        // Act
        var releasedCount = await worker.ReleaseExpiredHoldsAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, releasedCount);
        var updatedListing = await dbContext.ResaleListings.FirstAsync(l => l.Id == listingSold.Id);
        Assert.Equal(ListingStatus.Sold, updatedListing.ListingStatus);
    }
}
