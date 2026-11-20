using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Infrastructure.Persistence;

namespace TicketShield.UnitTests.Features;

/// <summary>
/// One escrow per money bucket, with numbers that reconcile: every Released escrow has a Success payout,
/// so Inflow - Paid out - Refunded - Held equals the fees of the Released escrows.
/// Shared by the Admin Transactions and Admin Analytics tests so both pages are checked on the same data.
/// </summary>
internal static class AdminMoneyTestData
{
    public const decimal ExpectedInflow = 4_074_000m;      // Released + Refunded + every held status
    public const decimal ExpectedHeld = 1_869_000m;        // Locked 525k + Disputed 210k + RefundQueued 1,134k
    public const int ExpectedHeldCount = 3;
    public const decimal ExpectedPaidOut = 950_000m;       // the Success payout of the Released escrow
    public const decimal ExpectedRefunded = 1_155_000m;
    public const decimal ExpectedNetRevenue = 100_000m;    // fees of the Released escrow
    public const decimal ExpectedGmv = 1_050_000m;         // buyer paid on the Released escrow
    public const int ExpectedInDispute = 1;                // only the Disputed escrow, not the resolved one

    public static async Task SeedAsync(TicketShieldDbContext db, DateTimeOffset createdAt)
    {
        var org = new Organizer { Id = Guid.NewGuid(), Name = "Money Org", OfficialEmail = "money.org@test.local" };
        var buyer = new ShadowUser { Id = Guid.NewGuid(), FullName = "Money Buyer", Email = "money.buyer@test.local" };
        var seller = new ShadowUser { Id = Guid.NewGuid(), FullName = "Money Seller", Email = "money.seller@test.local" };
        var ev = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = org.Id,
            Name = "Money Concert",
            Venue = "Money Stadium",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(30),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(30).AddHours(3),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(29)
        };
        var tier = new TicketTier { Id = Guid.NewGuid(), EventId = ev.Id, TierName = "GA", OriginalPrice = 1_000_000m };

        db.Organizers.Add(org);
        db.ShadowUsers.AddRange(buyer, seller);
        db.Events.Add(ev);
        db.TicketTiers.Add(tier);

        EscrowTransaction AddEscrow(string code, EscrowStatus status, decimal buyerPaid, decimal buyerFee, decimal sellerFee)
        {
            var listing = new ResaleListing
            {
                Id = Guid.NewGuid(),
                EventId = ev.Id,
                TierId = tier.Id,
                SellerId = seller.Id,
                OriginalTicketCode = code,
                OriginalPrice = buyerPaid - buyerFee,
                ResalePrice = buyerPaid - buyerFee,
                ListingStatus = ListingStatus.Sold
            };
            var escrow = new EscrowTransaction
            {
                Id = Guid.NewGuid(),
                ListingId = listing.Id,
                BuyerId = buyer.Id,
                SellerId = seller.Id,
                OriginalTicketPrice = buyerPaid - buyerFee,
                BuyerFee = buyerFee,
                SellerFee = sellerFee,
                TotalBuyerPaid = buyerPaid,
                NetSellerPayout = buyerPaid - buyerFee - sellerFee,
                Status = status,
                PaymentReference = "PAY-" + code,
                CreatedAt = createdAt
            };
            db.ResaleListings.Add(listing);
            db.EscrowTransactions.Add(escrow);
            return escrow;
        }

        var released = AddEscrow("MONEY-RELEASED", EscrowStatus.Released, 1_050_000m, 50_000m, 50_000m);
        db.PayoutTransactions.Add(new PayoutTransaction
        {
            Id = Guid.NewGuid(),
            EscrowId = released.Id,
            SellerId = seller.Id,
            PayoutCode = "PO-MONEY-RELEASED",
            Amount = 950_000m,
            Status = PayoutStatus.Success,
            RecipientBankCode = "VCB",
            RecipientAccountNumber = "1234567890",
            RecipientAccountName = "MONEY SELLER",
            ProcessedAt = createdAt.AddDays(1),
            CreatedAt = createdAt
        });
        db.Disputes.Add(new Dispute
        {
            Id = Guid.NewGuid(),
            EscrowId = released.Id,
            BuyerId = buyer.Id,
            DisputeCode = "DSP-MONEY-RESOLVED",
            Status = DisputeStatus.Rejected,
            Description = "Rejected dispute, escrow paid out"
        });

        AddEscrow("MONEY-REFUNDED", EscrowStatus.Refunded, 1_155_000m, 55_000m, 55_000m);
        AddEscrow("MONEY-REFUND-QUEUED", EscrowStatus.RefundQueued, 1_134_000m, 54_000m, 54_000m);
        AddEscrow("MONEY-LOCKED", EscrowStatus.Locked, 525_000m, 25_000m, 25_000m);

        var disputed = AddEscrow("MONEY-DISPUTED", EscrowStatus.Disputed, 210_000m, 10_000m, 10_000m);
        db.Disputes.Add(new Dispute
        {
            Id = Guid.NewGuid(),
            EscrowId = disputed.Id,
            BuyerId = buyer.Id,
            DisputeCode = "DSP-MONEY-OPEN",
            Status = DisputeStatus.Open,
            Description = "Open dispute"
        });

        // No money reached the platform for these two.
        AddEscrow("MONEY-PENDING", EscrowStatus.Pending, 300_000m, 15_000m, 15_000m);
        AddEscrow("MONEY-EXPIRED", EscrowStatus.Expired, 400_000m, 20_000m, 20_000m);

        await db.SaveChangesAsync();
    }
}
