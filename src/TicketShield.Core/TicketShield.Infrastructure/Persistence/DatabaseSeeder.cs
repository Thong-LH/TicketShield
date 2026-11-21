using Microsoft.EntityFrameworkCore;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;

namespace TicketShield.Infrastructure.Persistence;

public static class DatabaseSeeder
{
    public static async Task SeedTicketShieldAsync(TicketShieldDbContext context)
    {
        // 1. Auto-apply any pending migrations
        await context.Database.MigrateAsync();

        // 2. Seed / Sync ShadowUsers for Trading Core
        var sellerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var buyerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var adminId = Guid.Parse("99999999-9999-9999-9999-999999999999");

        var shadowSeller = await context.ShadowUsers.FirstOrDefaultAsync(u => u.Id == sellerId);
        if (shadowSeller == null)
        {
            shadowSeller = new ShadowUser { Id = sellerId };
            await context.ShadowUsers.AddAsync(shadowSeller);
        }
        shadowSeller.Email = "linhtranlatao2004@gmail.com";
        shadowSeller.FullName = "Nguyen Van Seller";
        shadowSeller.PhoneNumber = "0901234567";
        shadowSeller.Role = UserRole.User;
        shadowSeller.IsActive = true;

        var shadowBuyer = await context.ShadowUsers.FirstOrDefaultAsync(u => u.Id == buyerId);
        if (shadowBuyer == null)
        {
            shadowBuyer = new ShadowUser { Id = buyerId };
            await context.ShadowUsers.AddAsync(shadowBuyer);
        }
        shadowBuyer.Email = "buyer@ticketshield.vn";
        shadowBuyer.FullName = "Tran Thi Buyer";
        shadowBuyer.PhoneNumber = "0987654321";
        shadowBuyer.Role = UserRole.User;
        shadowBuyer.IsActive = true;

        var shadowAdmin = await context.ShadowUsers.FirstOrDefaultAsync(u => u.Id == adminId);
        if (shadowAdmin == null)
        {
            shadowAdmin = new ShadowUser { Id = adminId };
            await context.ShadowUsers.AddAsync(shadowAdmin);
        }
        shadowAdmin.Email = "admin@ticketshield.vn";
        shadowAdmin.FullName = "System Administrator";
        shadowAdmin.PhoneNumber = "0999999999";
        shadowAdmin.Role = UserRole.Admin;
        shadowAdmin.IsActive = true;

        var moderatorId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var shadowModerator = await context.ShadowUsers.FirstOrDefaultAsync(u => u.Id == moderatorId);
        if (shadowModerator == null)
        {
            shadowModerator = new ShadowUser { Id = moderatorId };
            await context.ShadowUsers.AddAsync(shadowModerator);
        }
        shadowModerator.Email = "moderator@ticketshield.vn";
        shadowModerator.FullName = "Le Thi CSKH & Moderator";
        shadowModerator.PhoneNumber = "0933333333";
        shadowModerator.Role = UserRole.Moderator;
        shadowModerator.IsActive = true;

        await context.SaveChangesAsync();

        // 3. Seed / Sync Organizer, Concert Events & Ticket Tiers
        var organizerId = Guid.Parse("e0000000-0000-0000-0000-000000000001");
        var eventId = Guid.Parse("e1111111-1111-1111-1111-111111111111");
        var tierVipId = Guid.Parse("d1111111-1111-1111-1111-111111111111");
        var tierGaId = Guid.Parse("d2222222-2222-2222-2222-222222222222");

        var organizer = await context.Organizers.FirstOrDefaultAsync(o => o.Id == organizerId);
        if (organizer == null)
        {
            organizer = new Organizer { Id = organizerId };
            await context.Organizers.AddAsync(organizer);
        }
        organizer.Name = "VieON Entertainment";
        organizer.OfficialEmail = "contact@vieon.vn";
        organizer.ContactPhone = "19001234";
        organizer.ApiKeyHash = "mock_api_key_hash_123456";
        organizer.WebhookUrl = "http://localhost:5001/api/v1/webhook";
        organizer.ApiBaseUrl = "http://localhost:5001";
        organizer.GrpcEndpoint = "http://localhost:5004";
        organizer.TaxCode = "0312345678";
        organizer.ContactPerson = "Nguyễn Văn Đại Diện";
        organizer.LastPingAt = DateTimeOffset.UtcNow;
        organizer.LastPingLatencyMs = 12;
        organizer.LastCatalogSyncedAt = DateTimeOffset.UtcNow;
        organizer.ConnectionStatus = "CONNECTED";
        organizer.Status = "ACTIVE";

        // Organizer 2 (Yêu cầu Review 5: Giả lập 2 nhà tổ chức để kiểm tra QR)
        var organizer2Id = Guid.Parse("e0000000-0000-0000-0000-000000000002");
        var organizer2 = await context.Organizers.FirstOrDefaultAsync(o => o.Id == organizer2Id);
        if (organizer2 == null)
        {
            organizer2 = new Organizer { Id = organizer2Id };
            await context.Organizers.AddAsync(organizer2);
        }
        organizer2.Name = "Ticketbox Vietnam";
        organizer2.OfficialEmail = "partner@ticketbox.vn";
        organizer2.ContactPhone = "19006408";
        organizer2.TaxCode = "0398765432";
        organizer2.ContactPerson = "Trần Thị Đối Tác";
        organizer2.ApiKeyHash = "mock_api_key_hash_ticketbox_2026";
        organizer2.WebhookUrl = "http://localhost:5001/api/v1/webhook";
        organizer2.ApiBaseUrl = "http://localhost:5001";
        organizer2.GrpcEndpoint = "http://localhost:5004";
        organizer2.WebhookSecretKey = "whsec_mock_organizer_ticketbox_2026";
        organizer2.LastPingAt = DateTimeOffset.UtcNow;
        organizer2.LastPingLatencyMs = 18;
        organizer2.LastCatalogSyncedAt = DateTimeOffset.UtcNow;
        organizer2.ConnectionStatus = "CONNECTED";
        organizer2.Status = "ACTIVE";

        // Event 1: Anh Trai Say Hi
        // A deterministic future demo slot keeps marketplace inventory outside
        // the two-hour resale cutoff. Gate-only VIP tickets are dated separately
        // in MockOrganizer so they can be scanned on the current demo date.
        var now = DateTimeOffset.UtcNow;
        var demoEventStart = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1).AddHours(12), TimeSpan.Zero);
        var ev = await context.Events.FirstOrDefaultAsync(e => e.Id == eventId);
        if (ev == null)
        {
            ev = new Event { Id = eventId };
            await context.Events.AddAsync(ev);
        }
        ev.OrganizerId = organizerId;
        ev.ExternalEventId = "atsh-2026";
        ev.Name = "Anh Trai Say Hi Concert 2026";
        ev.Artist = "Anh Trai Say Hi All-Stars";
        ev.Category = "CONCERT";
        ev.City = "TP. Hồ Chí Minh";
        ev.BannerUrl = "https://images.unsplash.com/photo-1470225620780-dba8ba36b745?auto=format&fit=crop&w=1400&q=80";
        ev.Description = "Mega Concert Vietnam 2026 quy tụ dàn nghệ sĩ đỉnh cao";
        ev.Venue = "Van Hanh Mall Stadium, TP.HCM";
        ev.EventStartAt = demoEventStart;
        ev.EventEndAt = demoEventStart.AddHours(4);
        ev.ResaleDeadline = demoEventStart.AddHours(-2);
        ev.Status = "UPCOMING";
        ev.MaxResaleMarkupPercentage = 10m;

        var tierVip = await context.TicketTiers.FirstOrDefaultAsync(t => t.Id == tierVipId);
        if (tierVip == null)
        {
            tierVip = new TicketTier { Id = tierVipId };
            await context.TicketTiers.AddAsync(tierVip);
        }
        tierVip.EventId = eventId;
        tierVip.TierName = "VIP Zone A";
        tierVip.OriginalPrice = 60000m; // TEST PRICE (prod: 2500000m)
        tierVip.Description = "Khu vực VIP sát sân khấu, tặng kèm lightstick";

        var tierGa = await context.TicketTiers.FirstOrDefaultAsync(t => t.Id == tierGaId);
        if (tierGa == null)
        {
            tierGa = new TicketTier { Id = tierGaId };
            await context.TicketTiers.AddAsync(tierGa);
        }
        tierGa.EventId = eventId;
        tierGa.TierName = "GA Standing";
        tierGa.OriginalPrice = 60000m; // TEST PRICE (prod: 1200000m)
        tierGa.Description = "Khu vực đứng tự do";

        // Event 2: My Tam Live Concert (Hanoi)
        var event2Id = Guid.Parse("e2222222-2222-2222-2222-222222222222");
        var ev2 = await context.Events.FirstOrDefaultAsync(e => e.Id == event2Id);
        if (ev2 == null)
        {
            ev2 = new Event { Id = event2Id };
            await context.Events.AddAsync(ev2);
        }
        ev2.OrganizerId = organizerId;
        ev2.Name = "Tri Âm Live Concert 2026";
        ev2.Artist = "Mỹ Tâm";
        ev2.Category = "CONCERT";
        ev2.City = "Hà Nội";
        ev2.BannerUrl = "https://images.unsplash.com/photo-1514525253161-7a46d19cd819?auto=format&fit=crop&w=1400&q=80";
        ev2.Description = "Live Concert âm nhạc kỷ niệm đặc biệt tại thủ đô";
        ev2.Venue = "Sân vận động Mỹ Đình, Hà Nội";
        ev2.EventStartAt = DateTimeOffset.UtcNow.AddDays(15);
        ev2.EventEndAt = DateTimeOffset.UtcNow.AddDays(15).AddHours(4);
        ev2.ResaleDeadline = DateTimeOffset.UtcNow.AddDays(15).AddHours(-2);
        ev2.Status = "UPCOMING";

        var tier2GaId = Guid.Parse("d3333333-3333-3333-3333-333333333333");
        var tier2Ga = await context.TicketTiers.FirstOrDefaultAsync(t => t.Id == tier2GaId);
        if (tier2Ga == null)
        {
            tier2Ga = new TicketTier { Id = tier2GaId };
            await context.TicketTiers.AddAsync(tier2Ga);
        }
        tier2Ga.EventId = event2Id;
        tier2Ga.TierName = "Khán đài A";
        tier2Ga.OriginalPrice = 1500000m;
        tier2Ga.Description = "Ghế ngồi khán đài chính diện sân khấu";

        // Event 3: Ravolution EDM Festival
        var event3Id = Guid.Parse("e3333333-3333-3333-3333-333333333333");
        var ev3 = await context.Events.FirstOrDefaultAsync(e => e.Id == event3Id);
        if (ev3 == null)
        {
            ev3 = new Event { Id = event3Id };
            await context.Events.AddAsync(ev3);
        }
        ev3.OrganizerId = organizerId;
        ev3.Name = "Ravolution Music Festival 2026";
        ev3.Artist = "International DJ Lineup";
        ev3.Category = "FESTIVAL";
        ev3.City = "TP. Hồ Chí Minh";
        ev3.BannerUrl = "https://images.unsplash.com/photo-1501386761578-eac5c94b800a?auto=format&fit=crop&w=1400&q=80";
        ev3.Description = "Lễ hội âm nhạc điện tử ngoài trời lớn nhất năm";
        ev3.Venue = "SECC Quận 7, TP.HCM";
        ev3.EventStartAt = DateTimeOffset.UtcNow.AddDays(20);
        ev3.EventEndAt = DateTimeOffset.UtcNow.AddDays(20).AddHours(8);
        ev3.ResaleDeadline = DateTimeOffset.UtcNow.AddDays(20).AddHours(-2);
        ev3.Status = "UPCOMING";

        var tier3GaId = Guid.Parse("d4444444-4444-4444-4444-444444444444");
        var tier3Ga = await context.TicketTiers.FirstOrDefaultAsync(t => t.Id == tier3GaId);
        if (tier3Ga == null)
        {
            tier3Ga = new TicketTier { Id = tier3GaId };
            await context.TicketTiers.AddAsync(tier3Ga);
        }
        tier3Ga.EventId = event3Id;
        tier3Ga.TierName = "General Admission";
        tier3Ga.OriginalPrice = 850000m;
        tier3Ga.Description = "Vé vào cổng tự do";

        // Event 4: V-League Derby
        var event4Id = Guid.Parse("e4444444-4444-4444-4444-444444444444");
        var ev4 = await context.Events.FirstOrDefaultAsync(e => e.Id == event4Id);
        if (ev4 == null)
        {
            ev4 = new Event { Id = event4Id };
            await context.Events.AddAsync(ev4);
        }
        ev4.OrganizerId = organizerId;
        ev4.Name = "Trận Derby: CLB Hà Nội vs CLB Viettel";
        ev4.Artist = "V-League 2026";
        ev4.Category = "SPORTS";
        ev4.City = "Hà Nội";
        ev4.BannerUrl = "https://images.unsplash.com/photo-1508098682722-e99c43a406b2?auto=format&fit=crop&w=1400&q=80";
        ev4.Description = "Trận cầu đinh giải vô địch bóng đá quốc gia";
        ev4.Venue = "Sân vận động Hàng Đẫy, Hà Nội";
        ev4.EventStartAt = DateTimeOffset.UtcNow.AddDays(7);
        ev4.EventEndAt = DateTimeOffset.UtcNow.AddDays(7).AddHours(2);
        ev4.ResaleDeadline = DateTimeOffset.UtcNow.AddDays(7).AddHours(-2);
        ev4.Status = "UPCOMING";

        var tier4GaId = Guid.Parse("d5555555-5555-5555-5555-555555555555");
        var tier4Ga = await context.TicketTiers.FirstOrDefaultAsync(t => t.Id == tier4GaId);
        if (tier4Ga == null)
        {
            tier4Ga = new TicketTier { Id = tier4GaId };
            await context.TicketTiers.AddAsync(tier4Ga);
        }
        tier4Ga.EventId = event4Id;
        tier4Ga.TierName = "Khán đài B";
        tier4Ga.OriginalPrice = 200000m;
        tier4Ga.Description = "Ghế ngồi khán đài B";

        await context.SaveChangesAsync();

        // 4. Seed IDs cho các listing mẫu & 3 vé COMBO sạch
        var sampleListingId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var sellerPlaceholderListing1Id = Guid.Parse("cc111111-1111-1111-1111-111111111111");
        var sellerPlaceholderListing2Id = Guid.Parse("cc222222-2222-2222-2222-222222222222");
        var sellerPlaceholderListing3Id = Guid.Parse("cc333333-3333-3333-3333-333333333333");

        // Seed IDs cho Cặp 2 vé bán lẻ trên sàn (IsBundleAllOrNothing = false)
        var pairTicket1Id = Guid.Parse("77777777-7777-7777-7777-777777777701");
        var pairTicket2Id = Guid.Parse("77777777-7777-7777-7777-777777777702");
        var bundlePairId = Guid.Parse("bb111111-1111-1111-1111-111111111111");
        var pairListing1Id = Guid.Parse("bb111111-1111-1111-1111-222222222201");
        var pairListing2Id = Guid.Parse("bb111111-1111-1111-1111-222222222202");

        var sampleTicketId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var sellerPlaceholderTicket1Id = Guid.Parse("cc111111-1111-1111-1111-888888888888");
        var sellerPlaceholderTicket2Id = Guid.Parse("cc222222-2222-2222-2222-888888888888");
        var sellerPlaceholderTicket3Id = Guid.Parse("cc333333-3333-3333-3333-888888888888");

        var seedEscrow1Id = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeee01");
        var seedEscrow2Id = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeee02");
        var seedEscrow3Id = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeee03");

        // Dọn dẹp các tin đăng và giao dịch rác từ các lần test trước để dữ liệu luôn sạch
        var seedCodes = new[] { "ATSH-VIP-888", "ATSH-VIP-887", "ATSH-VIP-886", "ATSH-GA-999", "ATSH-GA-001", "ATSH-GA-002" };
        var staleListings = await context.ResaleListings
            .Where(l => l.Id != sampleListingId &&
                        l.Id != sellerPlaceholderListing1Id &&
                        l.Id != sellerPlaceholderListing2Id &&
                        l.Id != sellerPlaceholderListing3Id &&
                        l.Id != pairListing1Id &&
                        l.Id != pairListing2Id &&
                        seedCodes.Contains(l.OriginalTicketCode))
            .ToListAsync();
        if (staleListings.Any())
        {
            var staleIds = staleListings.Select(l => l.Id).ToList();
            var staleEtls = await context.EscrowTransactionListings
                .Where(etl => staleIds.Contains(etl.ListingId))
                .ToListAsync();
            context.EscrowTransactionListings.RemoveRange(staleEtls);

            var staleEscrows = await context.EscrowTransactions
                .Where(e => staleIds.Contains(e.ListingId))
                .ToListAsync();
            context.EscrowTransactions.RemoveRange(staleEscrows);

            context.ResaleListings.RemoveRange(staleListings);
        }

        var danglingHolds = await context.EscrowTransactions
            .Where(e => (e.ListingId == sampleListingId ||
                         e.ListingId == sellerPlaceholderListing1Id ||
                         e.ListingId == sellerPlaceholderListing2Id ||
                         e.ListingId == sellerPlaceholderListing3Id ||
                         e.ListingId == pairListing1Id ||
                         e.ListingId == pairListing2Id) &&
                        e.Status == EscrowStatus.Pending)
            .ToListAsync();
        if (danglingHolds.Any())
        {
            var danglingHoldIds = danglingHolds.Select(e => e.Id).ToList();
            var danglingHoldMappings = await context.EscrowTransactionListings
                .Where(etl => danglingHoldIds.Contains(etl.EscrowId))
                .ToListAsync();
            context.EscrowTransactionListings.RemoveRange(danglingHoldMappings);
            context.EscrowTransactions.RemoveRange(danglingHolds);
        }

        // US-12.2 / US-12.3: Seed Real Ticket Entities for Marketplace and Seller listings
        var sampleTicket = await context.Tickets.FirstOrDefaultAsync(t => t.Id == sampleTicketId);
        if (sampleTicket == null)
        {
            sampleTicket = new Ticket { Id = sampleTicketId };
            await context.Tickets.AddAsync(sampleTicket);
        }
        sampleTicket.EventId = eventId;
        sampleTicket.TierId = tierGaId;
        sampleTicket.CurrentOwnerId = sellerId;
        sampleTicket.TicketCode = "ATSH-GA-999";
        sampleTicket.SeatZone = "GA Standing Zone 2";
        sampleTicket.OriginalPrice = 60000m;
        sampleTicket.Status = TicketStatus.LockedForResale;

        var splTicket1 = await context.Tickets.FirstOrDefaultAsync(t => t.Id == sellerPlaceholderTicket1Id);
        if (splTicket1 == null)
        {
            splTicket1 = new Ticket { Id = sellerPlaceholderTicket1Id };
            await context.Tickets.AddAsync(splTicket1);
        }
        splTicket1.EventId = eventId;
        splTicket1.TierId = tierVipId;
        // These are three tickets previously purchased by the demo seller, ready
        // to be selected together in the seller's combo flow.
        splTicket1.CurrentOwnerId = sellerId;
        splTicket1.TicketCode = "SEED-PLACEHOLDER-888";
        splTicket1.SeatZone = "VIP Zone A - Row 1 Seat 12";
        splTicket1.OriginalPrice = 60000m;
        splTicket1.Status = TicketStatus.Valid;

        var splTicket2 = await context.Tickets.FirstOrDefaultAsync(t => t.Id == sellerPlaceholderTicket2Id);
        if (splTicket2 == null)
        {
            splTicket2 = new Ticket { Id = sellerPlaceholderTicket2Id };
            await context.Tickets.AddAsync(splTicket2);
        }
        splTicket2.EventId = eventId;
        splTicket2.TierId = tierVipId;
        splTicket2.CurrentOwnerId = sellerId;
        splTicket2.TicketCode = "SEED-PLACEHOLDER-887";
        splTicket2.SeatZone = "VIP Zone A - Row 2 Seat 08";
        splTicket2.OriginalPrice = 60000m;
        splTicket2.Status = TicketStatus.Valid;

        var splTicket3 = await context.Tickets.FirstOrDefaultAsync(t => t.Id == sellerPlaceholderTicket3Id);
        if (splTicket3 == null)
        {
            splTicket3 = new Ticket { Id = sellerPlaceholderTicket3Id };
            await context.Tickets.AddAsync(splTicket3);
        }
        splTicket3.EventId = eventId;
        splTicket3.TierId = tierVipId;
        splTicket3.CurrentOwnerId = sellerId;
        splTicket3.TicketCode = "SEED-PLACEHOLDER-886";
        splTicket3.SeatZone = "VIP Zone A - Row 2 Seat 09";
        splTicket3.OriginalPrice = 60000m;
        splTicket3.Status = TicketStatus.Valid;

        // Tickets cho Cặp 2 vé bán lẻ trên sàn
        var pairTicket1 = await context.Tickets.FirstOrDefaultAsync(t => t.Id == pairTicket1Id);
        if (pairTicket1 == null)
        {
            pairTicket1 = new Ticket { Id = pairTicket1Id };
            await context.Tickets.AddAsync(pairTicket1);
        }
        pairTicket1.EventId = eventId;
        pairTicket1.TierId = tierGaId;
        pairTicket1.CurrentOwnerId = sellerId;
        pairTicket1.TicketCode = "ATSH-GA-001";
        pairTicket1.SeatZone = "GA Standing Zone 1 - No. 01";
        pairTicket1.OriginalPrice = 60000m;
        pairTicket1.Status = TicketStatus.LockedForResale;

        var pairTicket2 = await context.Tickets.FirstOrDefaultAsync(t => t.Id == pairTicket2Id);
        if (pairTicket2 == null)
        {
            pairTicket2 = new Ticket { Id = pairTicket2Id };
            await context.Tickets.AddAsync(pairTicket2);
        }
        pairTicket2.EventId = eventId;
        pairTicket2.TierId = tierGaId;
        pairTicket2.CurrentOwnerId = sellerId;
        pairTicket2.TicketCode = "ATSH-GA-002";
        pairTicket2.SeatZone = "GA Standing Zone 1 - No. 02";
        pairTicket2.OriginalPrice = 60000m;
        pairTicket2.Status = TicketStatus.LockedForResale;

        await context.SaveChangesAsync();

        // 5.1 Seed Sample Single Resale Listing (ATSH-GA-999) for marketplace demo
        var sampleListing = await context.ResaleListings.FirstOrDefaultAsync(l => l.Id == sampleListingId);
        if (sampleListing == null)
        {
            sampleListing = new ResaleListing { Id = sampleListingId };
            await context.ResaleListings.AddAsync(sampleListing);
        }
        sampleListing.TicketId = sampleTicketId;
        sampleListing.EventId = eventId;
        sampleListing.TierId = tierGaId;
        sampleListing.SellerId = sellerId;
        sampleListing.OriginalTicketCode = "ATSH-GA-999";
        sampleListing.OriginalPrice = 60000m;
        sampleListing.ResalePrice = 60000m;
        sampleListing.AppliedMarkupPercentage = 0m;
        sampleListing.IsPrivate = false;
        sampleListing.PrivateAccessToken = null;
        sampleListing.VerificationStatus = VerificationStatus.Verified;
        sampleListing.ListingStatus = ListingStatus.Verified;
        sampleListing.SeatZone = "GA Standing Zone 2";

        // 5.1.2 Seed Sample Pair Bundle (2 vé bán lẻ IsBundleAllOrNothing = false)
        var pairListing1 = await context.ResaleListings.FirstOrDefaultAsync(l => l.Id == pairListing1Id);
        if (pairListing1 == null)
        {
            pairListing1 = new ResaleListing { Id = pairListing1Id };
            await context.ResaleListings.AddAsync(pairListing1);
        }
        pairListing1.TicketId = pairTicket1Id;
        pairListing1.EventId = eventId;
        pairListing1.TierId = tierGaId;
        pairListing1.SellerId = sellerId;
        pairListing1.OriginalTicketCode = "ATSH-GA-001";
        pairListing1.OriginalPrice = 60000m;
        pairListing1.ResalePrice = 60000m;
        pairListing1.AppliedMarkupPercentage = 0m;
        pairListing1.IsPrivate = false;
        pairListing1.PrivateAccessToken = null;
        pairListing1.VerificationStatus = VerificationStatus.Verified;
        pairListing1.ListingStatus = ListingStatus.Verified;
        pairListing1.SeatZone = "GA Standing Zone 1 - No. 01";
        pairListing1.BundleId = bundlePairId;
        pairListing1.BundleTotalTickets = 2;
        pairListing1.IsBundleAllOrNothing = false;

        var pairListing2 = await context.ResaleListings.FirstOrDefaultAsync(l => l.Id == pairListing2Id);
        if (pairListing2 == null)
        {
            pairListing2 = new ResaleListing { Id = pairListing2Id };
            await context.ResaleListings.AddAsync(pairListing2);
        }
        pairListing2.TicketId = pairTicket2Id;
        pairListing2.EventId = eventId;
        pairListing2.TierId = tierGaId;
        pairListing2.SellerId = sellerId;
        pairListing2.OriginalTicketCode = "ATSH-GA-002";
        pairListing2.OriginalPrice = 60000m;
        pairListing2.ResalePrice = 60000m;
        pairListing2.AppliedMarkupPercentage = 0m;
        pairListing2.IsPrivate = false;
        pairListing2.PrivateAccessToken = null;
        pairListing2.VerificationStatus = VerificationStatus.Verified;
        pairListing2.ListingStatus = ListingStatus.Verified;
        pairListing2.SeatZone = "GA Standing Zone 1 - No. 02";
        pairListing2.BundleId = bundlePairId;
        pairListing2.BundleTotalTickets = 2;
        pairListing2.IsBundleAllOrNothing = false;

        await context.SaveChangesAsync();

        // 5.2 Seed Mock Purchased EscrowTransactions for Seller account (3 vé sẵn sàng cho combo)
        var spl1 = await context.ResaleListings.FirstOrDefaultAsync(l => l.Id == sellerPlaceholderListing1Id);
        if (spl1 == null)
        {
            spl1 = new ResaleListing { Id = sellerPlaceholderListing1Id };
            await context.ResaleListings.AddAsync(spl1);
        }
        spl1.TicketId = sellerPlaceholderTicket1Id;
        spl1.EventId = eventId;
        spl1.TierId = tierVipId;
        spl1.SellerId = buyerId;
        spl1.OriginalTicketCode = "SEED-PLACEHOLDER-888";
        spl1.OriginalPrice = 60000m;
        spl1.ResalePrice = 60000m;
        spl1.AppliedMarkupPercentage = 0m;
        spl1.VerificationStatus = VerificationStatus.Verified;
        spl1.ListingStatus = ListingStatus.Cancelled;
        spl1.SeatZone = "VIP Zone A - Row 1 Seat 12";

        var spl2 = await context.ResaleListings.FirstOrDefaultAsync(l => l.Id == sellerPlaceholderListing2Id);
        if (spl2 == null)
        {
            spl2 = new ResaleListing { Id = sellerPlaceholderListing2Id };
            await context.ResaleListings.AddAsync(spl2);
        }
        spl2.TicketId = sellerPlaceholderTicket2Id;
        spl2.EventId = eventId;
        spl2.TierId = tierVipId;
        spl2.SellerId = buyerId;
        spl2.OriginalTicketCode = "SEED-PLACEHOLDER-887";
        spl2.OriginalPrice = 60000m;
        spl2.ResalePrice = 60000m;
        spl2.AppliedMarkupPercentage = 0m;
        spl2.VerificationStatus = VerificationStatus.Verified;
        spl2.ListingStatus = ListingStatus.Cancelled;
        spl2.SeatZone = "VIP Zone A - Row 2 Seat 08";

        var spl3 = await context.ResaleListings.FirstOrDefaultAsync(l => l.Id == sellerPlaceholderListing3Id);
        if (spl3 == null)
        {
            spl3 = new ResaleListing { Id = sellerPlaceholderListing3Id };
            await context.ResaleListings.AddAsync(spl3);
        }
        spl3.TicketId = sellerPlaceholderTicket3Id;
        spl3.EventId = eventId;
        spl3.TierId = tierVipId;
        spl3.SellerId = buyerId;
        spl3.OriginalTicketCode = "SEED-PLACEHOLDER-886";
        spl3.OriginalPrice = 60000m;
        spl3.ResalePrice = 60000m;
        spl3.AppliedMarkupPercentage = 0m;
        spl3.VerificationStatus = VerificationStatus.Verified;
        spl3.ListingStatus = ListingStatus.Cancelled;
        spl3.SeatZone = "VIP Zone A - Row 2 Seat 09";

        await context.SaveChangesAsync();

        var seedEscrow1 = await context.EscrowTransactions.FirstOrDefaultAsync(e => e.Id == seedEscrow1Id);
        if (seedEscrow1 == null)
        {
            seedEscrow1 = new EscrowTransaction { Id = seedEscrow1Id };
            await context.EscrowTransactions.AddAsync(seedEscrow1);
        }
        seedEscrow1.ListingId = sellerPlaceholderListing1Id;
        seedEscrow1.BuyerId = sellerId;
        seedEscrow1.SellerId = buyerId;
        seedEscrow1.OriginalTicketPrice = 60000m;
        seedEscrow1.BuyerFee = 3000m;
        seedEscrow1.SellerFee = 6000m;
        seedEscrow1.TotalBuyerPaid = 63000m;
        seedEscrow1.NetSellerPayout = 54000m;
        seedEscrow1.Status = EscrowStatus.Released;
        seedEscrow1.NewTicketCode = "ATSH-VIP-888";
        seedEscrow1.QrCodeData = "ATSH-VIP-888";
        seedEscrow1.BankTransactionReference = "BTC-SEED-TX-888";
        seedEscrow1.RecipientName = "Nguyen Van Seller";
        seedEscrow1.RecipientEmail = "linhtranlatao2004@gmail.com";

        var seedEscrow2 = await context.EscrowTransactions.FirstOrDefaultAsync(e => e.Id == seedEscrow2Id);
        if (seedEscrow2 == null)
        {
            seedEscrow2 = new EscrowTransaction { Id = seedEscrow2Id };
            await context.EscrowTransactions.AddAsync(seedEscrow2);
        }
        seedEscrow2.ListingId = sellerPlaceholderListing2Id;
        seedEscrow2.BuyerId = sellerId;
        seedEscrow2.SellerId = buyerId;
        seedEscrow2.OriginalTicketPrice = 60000m;
        seedEscrow2.BuyerFee = 3000m;
        seedEscrow2.SellerFee = 6000m;
        seedEscrow2.TotalBuyerPaid = 63000m;
        seedEscrow2.NetSellerPayout = 54000m;
        seedEscrow2.Status = EscrowStatus.Released;
        seedEscrow2.NewTicketCode = "ATSH-VIP-887";
        seedEscrow2.QrCodeData = "ATSH-VIP-887";
        seedEscrow2.BankTransactionReference = "BTC-SEED-TX-887";
        seedEscrow2.RecipientName = "Nguyen Van Seller";
        seedEscrow2.RecipientEmail = "linhtranlatao2004@gmail.com";

        var seedEscrow3 = await context.EscrowTransactions.FirstOrDefaultAsync(e => e.Id == seedEscrow3Id);
        if (seedEscrow3 == null)
        {
            seedEscrow3 = new EscrowTransaction { Id = seedEscrow3Id };
            await context.EscrowTransactions.AddAsync(seedEscrow3);
        }
        seedEscrow3.ListingId = sellerPlaceholderListing3Id;
        seedEscrow3.BuyerId = sellerId;
        seedEscrow3.SellerId = buyerId;
        seedEscrow3.OriginalTicketPrice = 60000m;
        seedEscrow3.BuyerFee = 3000m;
        seedEscrow3.SellerFee = 6000m;
        seedEscrow3.TotalBuyerPaid = 63000m;
        seedEscrow3.NetSellerPayout = 54000m;
        seedEscrow3.Status = EscrowStatus.Released;
        seedEscrow3.NewTicketCode = "ATSH-VIP-886";
        seedEscrow3.QrCodeData = "ATSH-VIP-886";
        seedEscrow3.BankTransactionReference = "BTC-SEED-TX-886";
        seedEscrow3.RecipientName = "Nguyen Van Seller";
        seedEscrow3.RecipientEmail = "linhtranlatao2004@gmail.com";

        await context.SaveChangesAsync();

        // Seed EscrowTransactionListings mappings
        var etl1 = await context.EscrowTransactionListings.FirstOrDefaultAsync(x => x.EscrowId == seedEscrow1Id && x.ListingId == sellerPlaceholderListing1Id);
        if (etl1 == null)
        {
            await context.EscrowTransactionListings.AddAsync(new EscrowTransactionListing { EscrowId = seedEscrow1Id, ListingId = sellerPlaceholderListing1Id });
        }
        var etl2 = await context.EscrowTransactionListings.FirstOrDefaultAsync(x => x.EscrowId == seedEscrow2Id && x.ListingId == sellerPlaceholderListing2Id);
        if (etl2 == null)
        {
            await context.EscrowTransactionListings.AddAsync(new EscrowTransactionListing { EscrowId = seedEscrow2Id, ListingId = sellerPlaceholderListing2Id });
        }
        var etl3 = await context.EscrowTransactionListings.FirstOrDefaultAsync(x => x.EscrowId == seedEscrow3Id && x.ListingId == sellerPlaceholderListing3Id);
        if (etl3 == null)
        {
            await context.EscrowTransactionListings.AddAsync(new EscrowTransactionListing { EscrowId = seedEscrow3Id, ListingId = sellerPlaceholderListing3Id });
        }

        await context.SaveChangesAsync();

        // 6. Seed Default Resale Fee System Settings (BE-CORE-2.6.2 & Seller Fee 10%)
        var feeSettings = new[]
        {
            new { Key = "ResaleFee_BuyerPercentage", Value = "0.05", Type = "Decimal", Desc = "Tỷ lệ phí người mua (5%)" },
            new { Key = "ResaleFee_SellerPercentage", Value = "0.10", Type = "Decimal", Desc = "Tỷ lệ phí người bán (10%)" },
            // No minimum applies to the low-value demo tickets, so the visible
            // amounts remain exactly Buyer 5% and Seller 10%.
            new { Key = "ResaleFee_MinBuyerFee", Value = "0", Type = "Money", Desc = "Không áp dụng phí tối thiểu cho dữ liệu demo" },
            new { Key = "ResaleFee_MinSellerFee", Value = "0", Type = "Money", Desc = "Không áp dụng phí tối thiểu cho dữ liệu demo" },
            new { Key = "EscrowBuffer_SettlementSeconds", Value = "86400", Type = "Integer", Desc = "Số giây đệm trước khi trả tiền người bán (24 giờ)" },
            new { Key = "EscrowBuffer_CutoffSeconds", Value = "7200", Type = "Integer", Desc = "Số giây trước giờ diễn đóng sàn giao dịch vé (2 giờ)" }
        };

        foreach (var item in feeSettings)
        {
            var setting = await context.SystemSettings.FirstOrDefaultAsync(s => s.SettingKey == item.Key);
            if (setting == null)
            {
                await context.SystemSettings.AddAsync(new SystemSetting
                {
                    SettingKey = item.Key,
                    SettingValue = item.Value,
                    DataType = item.Type,
                    Description = item.Desc
                });
            }
            else
            {
                setting.SettingValue = item.Value;
                setting.DataType = item.Type;
                setting.Description = item.Desc;
            }
        }

        await context.SaveChangesAsync();
    }
}
