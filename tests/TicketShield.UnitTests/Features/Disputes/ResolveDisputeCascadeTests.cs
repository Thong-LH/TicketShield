using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.Disputes.Commands.ResolveDispute;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.Features.Disputes;

public class ResolveDisputeCascadeTests
{
    [Fact]
    public async Task Handle_WhenDisputeApproved_ShouldCascadeCancelDownstreamListingsAndTickets()
    {
        // Arrange: Buyer mua vé "TCK-CASCADE-001", sau đó đăng bán lại tạo listing hạ nguồn
        await using var db = CreateDb();

        var adminId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var tierId = Guid.NewGuid();

        var admin = new ShadowUser { Id = adminId, Email = "admin@test.local", FullName = "Admin", Role = UserRole.Admin };
        var seller = new ShadowUser { Id = sellerId, Email = "seller@test.local", FullName = "Seller", PayoutAccountNumber = "123" };
        var buyer = new ShadowUser { Id = buyerId, Email = "buyer@test.local", FullName = "Buyer", PayoutAccountNumber = "456" };

        var organizer = new Organizer { Id = Guid.NewGuid(), Name = "Org", OfficialEmail = "org@test.local" };
        var concert = new Event
        {
            Id = eventId,
            OrganizerId = organizer.Id,
            Name = "Concert",
            Venue = "Venue",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(5),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(5).AddHours(2),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(4)
        };
        var tier = new TicketTier { Id = tierId, EventId = eventId, TierName = "VIP", OriginalPrice = 1_000_000m };

        // Đơn mua gốc: Escrow cấp NewTicketCode = "TCK-CASCADE-001" cho Buyer
        var originalListing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            TierId = tierId,
            SellerId = sellerId,
            OriginalTicketCode = "ORIG-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_000_000m,
            ListingStatus = ListingStatus.Sold
        };

        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = originalListing.Id,
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = EscrowStatus.Disputed,
            NewTicketCode = "TCK-CASCADE-001",
            TotalBuyerPaid = 1_000_000m,
            NetSellerPayout = 900_000m
        };

        var dispute = new Dispute
        {
            Id = Guid.NewGuid(),
            EscrowId = escrow.Id,
            BuyerId = buyerId,
            DisputeCode = "DP-CASCADE-01",
            Status = DisputeStatus.Open,
            Description = "Vé không hợp lệ"
        };

        // Thực thể Ticket được cấp cho Buyer
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            TierId = tierId,
            CurrentOwnerId = buyerId,
            TicketCode = "TCK-CASCADE-001",
            OriginalPrice = 1_000_000m,
            Status = TicketStatus.LockedForResale
        };

        // Listing thứ cấp do Buyer tạo ra để bán lại vé TCK-CASCADE-001 trên sàn
        var downstreamListing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Ticket = ticket,
            EventId = eventId,
            TierId = tierId,
            SellerId = buyerId,
            OriginalTicketCode = "TCK-CASCADE-001",
            OriginalPrice = 1_000_000m,
            ResalePrice = 950_000m,
            ListingStatus = ListingStatus.Verified
        };

        db.Organizers.Add(organizer);
        db.Events.Add(concert);
        db.TicketTiers.Add(tier);
        db.ShadowUsers.AddRange(admin, seller, buyer);
        db.Tickets.Add(ticket);
        db.ResaleListings.AddRange(originalListing, downstreamListing);
        db.EscrowTransactions.Add(escrow);
        db.Disputes.Add(dispute);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new ResolveDisputeCommandHandler(db, new StubUser(adminId, "Admin", true));
        var command = new ResolveDisputeCommand
        {
            DisputeId = dispute.Id,
            Decision = DisputeDecision.Approve,
            Reason = "Chấp nhận hoàn tiền cho Buyer"
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);

        db.ChangeTracker.Clear();
        var reloadedDownstream = await db.ResaleListings.Include(l => l.Ticket).FirstOrDefaultAsync(l => l.Id == downstreamListing.Id);
        Assert.NotNull(reloadedDownstream);
        Assert.Equal(ListingStatus.Cancelled, reloadedDownstream.ListingStatus);

        var reloadedTicket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == ticket.Id);
        Assert.NotNull(reloadedTicket);
        Assert.Equal(TicketStatus.Cancelled, reloadedTicket.Status);
    }

    [Fact]
    public async Task Handle_WhenDisputeApproved_AndDownstreamListingIsTransacting_ShouldCancelHoldEscrowAndListing()
    {
        // Arrange: Buyer đăng bán lại, có Buyer 2 đang giữ chỗ (Transacting với Pending Escrow)
        await using var db = CreateDb();

        var adminId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var buyer2Id = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var tierId = Guid.NewGuid();

        var admin = new ShadowUser { Id = adminId, Email = "admin2@test.local", FullName = "Admin", Role = UserRole.Admin };
        var seller = new ShadowUser { Id = sellerId, Email = "seller2@test.local", FullName = "Seller", PayoutAccountNumber = "111" };
        var buyer = new ShadowUser { Id = buyerId, Email = "buyer2@test.local", FullName = "Buyer", PayoutAccountNumber = "222" };
        var buyer2 = new ShadowUser { Id = buyer2Id, Email = "buyer2_sub@test.local", FullName = "Buyer2", PayoutAccountNumber = "333" };

        var organizer = new Organizer { Id = Guid.NewGuid(), Name = "Org2", OfficialEmail = "org2@test.local" };
        var concert = new Event
        {
            Id = eventId,
            OrganizerId = organizer.Id,
            Name = "Concert 2",
            Venue = "Hall",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(5),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(5).AddHours(2),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(4)
        };
        var tier = new TicketTier { Id = tierId, EventId = eventId, TierName = "GA", OriginalPrice = 500_000m };

        var originalListing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            TierId = tierId,
            SellerId = sellerId,
            OriginalTicketCode = "ORIG-002",
            OriginalPrice = 500_000m,
            ResalePrice = 500_000m,
            ListingStatus = ListingStatus.Sold
        };

        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = originalListing.Id,
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = EscrowStatus.Disputed,
            NewTicketCode = "TCK-DOWNSTREAM-HOLD",
            TotalBuyerPaid = 500_000m,
            NetSellerPayout = 450_000m
        };

        var dispute = new Dispute
        {
            Id = Guid.NewGuid(),
            EscrowId = escrow.Id,
            BuyerId = buyerId,
            DisputeCode = "DP-CASCADE-02",
            Status = DisputeStatus.Open,
            Description = "Vé trùng lặp"
        };

        // Listing thứ cấp đang bị giữ chỗ (Transacting)
        var downstreamListing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            TierId = tierId,
            SellerId = buyerId,
            OriginalTicketCode = "TCK-DOWNSTREAM-HOLD",
            OriginalPrice = 500_000m,
            ResalePrice = 500_000m,
            ListingStatus = ListingStatus.Transacting
        };

        // Đơn giữ chỗ của Buyer 2
        var holdEscrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = downstreamListing.Id,
            BuyerId = buyer2Id,
            SellerId = buyerId,
            Status = EscrowStatus.Pending,
            TotalBuyerPaid = 500_000m,
            NetSellerPayout = 450_000m
        };

        db.Organizers.Add(organizer);
        db.Events.Add(concert);
        db.TicketTiers.Add(tier);
        db.ShadowUsers.AddRange(admin, seller, buyer, buyer2);
        db.ResaleListings.AddRange(originalListing, downstreamListing);
        db.EscrowTransactions.AddRange(escrow, holdEscrow);
        db.Disputes.Add(dispute);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new ResolveDisputeCommandHandler(db, new StubUser(adminId, "Admin", true));
        var command = new ResolveDisputeCommand
        {
            DisputeId = dispute.Id,
            Decision = DisputeDecision.Approve,
            Reason = "Đồng ý hoàn tiền"
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);

        db.ChangeTracker.Clear();
        var reloadedListing = await db.ResaleListings.FindAsync(downstreamListing.Id);
        Assert.NotNull(reloadedListing);
        Assert.Equal(ListingStatus.Cancelled, reloadedListing.ListingStatus);

        var reloadedHold = await db.EscrowTransactions.FindAsync(holdEscrow.Id);
        Assert.NotNull(reloadedHold);
        Assert.Equal(EscrowStatus.Cancelled, reloadedHold.Status);
    }

    private static TicketShieldDbContext CreateDb()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>().UseSqlite(connection).Options;
        var db = new TicketShieldDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private sealed class StubUser(Guid? userId, string? role, bool authenticated) : ICurrentUserService
    {
        public Guid? UserId { get; } = userId;
        public string? Email => "staff@test.local";
        public string? Role { get; } = role;
        public bool IsAuthenticated { get; } = authenticated;
    }
}
