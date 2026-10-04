using Microsoft.EntityFrameworkCore;
using MockOrganizer.API.Entities;

namespace MockOrganizer.API.Data;

public static class OrganizerDatabaseSeeder
{
    public static async Task SeedOrganizerAsync(OrganizerDbContext context)
    {
        // 1. Auto-apply any pending migrations on startup (code-first auto-init)
        await context.Database.MigrateAsync();

        // 2. Seed & Deterministic Reset of Mock Tickets
        // --- VieON ticket IDs ---
        var ticket1Id = Guid.Parse("a0000000-0000-0000-0000-000000000001");
        var ticket2Id = Guid.Parse("a0000000-0000-0000-0000-000000000002");
        var ticket3Id = Guid.Parse("a0000000-0000-0000-0000-000000000003");
        var ticket4Id = Guid.Parse("a0000000-0000-0000-0000-000000000004");
        var ticket5Id = Guid.Parse("a0000000-0000-0000-0000-000000000005");
        // --- SpaceSpeakers ticket IDs ---
        var ticket6Id  = Guid.Parse("a0000000-0000-0000-0000-000000000006");
        var ticket7Id  = Guid.Parse("a0000000-0000-0000-0000-000000000007");
        var ticket8Id  = Guid.Parse("a0000000-0000-0000-0000-000000000008");
        // --- Live Nation VN ticket IDs ---
        var ticket9Id  = Guid.Parse("a0000000-0000-0000-0000-000000000009");
        var ticket10Id = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
        var ticket11Id = Guid.Parse("a0000000-0000-0000-0000-00000000000b");

        var seedIds = new HashSet<Guid>
        {
            ticket1Id, ticket2Id, ticket3Id, ticket4Id, ticket5Id,  // VieON
            ticket6Id, ticket7Id, ticket8Id,                         // SpaceSpeakers
            ticket9Id, ticket10Id, ticket11Id                        // Live Nation VN
        };

        // US-5.1 Multi-Tenant: 3 independent organizer IDs
        var vieOnId         = Guid.Parse("e0000000-0000-0000-0000-000000000001"); // VieON (matches Core API)
        var spaceSpeakersId = Guid.Parse("b0000000-0000-0000-0000-000000000002"); // SpaceSpeakers
        var liveNationVnId  = Guid.Parse("b0000000-0000-0000-0000-000000000003"); // Live Nation VN

        // Clean operational logs, OTPs, and leftover resale locks so cancelled tickets can request OTP again
        context.MockOtps.RemoveRange(await context.MockOtps.ToListAsync());
        context.GateAccessLogs.RemoveRange(await context.GateAccessLogs.ToListAsync());
        await context.Database.ExecuteSqlRawAsync("DELETE FROM organizer_resale_records");

        // Drop leftover non-seed tickets, but keep VALID ones so a transferred buyer ticket survives restart.
        var extraTickets = await context.MockTickets
            .Where(t => !seedIds.Contains(t.Id) && t.Status != "VALID")
            .ToListAsync();
        if (extraTickets.Any())
        {
            context.MockTickets.RemoveRange(extraTickets);
        }

        // =====================================================================
        // BTC 1: VieON — Anh Trai Say Hi Concert 2026
        // =====================================================================
        var ticket1 = await context.MockTickets.FirstOrDefaultAsync(t => t.Id == ticket1Id);
        if (ticket1 == null)
        {
            ticket1 = new MockTicket { Id = ticket1Id };
            await context.MockTickets.AddAsync(ticket1);
        }
        ticket1.OrganizerId = vieOnId;
        ticket1.TicketCode = "ATSH-VIP-888";
        ticket1.EventName = "Anh Trai Say Hi Concert 2026";
        ticket1.SeatZone = "VIP Zone A - Row 1 Seat 12";
        ticket1.OriginalPrice = 50000;
        ticket1.OwnerEmail = "linhtranlatao2004@gmail.com";
        ticket1.OwnerPhone = "0901234567";
        ticket1.OwnerName = "Nguyen Van Seller";
        ticket1.Status = "VALID";

        var ticket2 = await context.MockTickets.FirstOrDefaultAsync(t => t.Id == ticket2Id);
        if (ticket2 == null)
        {
            ticket2 = new MockTicket { Id = ticket2Id };
            await context.MockTickets.AddAsync(ticket2);
        }
        ticket2.OrganizerId = vieOnId;
        ticket2.TicketCode = "ATSH-GA-999";
        ticket2.EventName = "Anh Trai Say Hi Concert 2026";
        ticket2.SeatZone = "GA Standing Zone 2";
        ticket2.OriginalPrice = 50000;
        ticket2.OwnerEmail = "linhtranlatao2004@gmail.com";
        ticket2.OwnerPhone = "0901234567";
        ticket2.OwnerName = "Nguyen Van Seller";
        ticket2.Status = "VALID";

        var ticket3 = await context.MockTickets.FirstOrDefaultAsync(t => t.Id == ticket3Id);
        if (ticket3 == null)
        {
            ticket3 = new MockTicket { Id = ticket3Id };
            await context.MockTickets.AddAsync(ticket3);
        }
        ticket3.OrganizerId = vieOnId;
        ticket3.TicketCode = "ATSH-USED-001";
        ticket3.EventName = "Anh Trai Say Hi Concert 2026";
        ticket3.SeatZone = "Standard Zone C";
        ticket3.OriginalPrice = 800000;
        ticket3.OwnerEmail = "linhtranlatao2004@gmail.com";
        ticket3.OwnerPhone = "0901234567";
        ticket3.OwnerName = "Nguyen Van Seller";
        ticket3.Status = "USED";

        var ticket4 = await context.MockTickets.FirstOrDefaultAsync(t => t.Id == ticket4Id);
        if (ticket4 == null)
        {
            ticket4 = new MockTicket { Id = ticket4Id };
            await context.MockTickets.AddAsync(ticket4);
        }
        ticket4.OrganizerId = vieOnId;
        ticket4.TicketCode = "ATSH-VIP-887";
        ticket4.EventName = "Anh Trai Say Hi Concert 2026";
        ticket4.SeatZone = "VIP Zone A - Row 2 Seat 08";
        ticket4.OriginalPrice = 2500000;
        ticket4.OwnerEmail = "linhtranlatao2004@gmail.com";
        ticket4.OwnerPhone = "0901234567";
        ticket4.OwnerName = "Nguyen Van Seller";
        ticket4.Status = "VALID";

        var ticket5 = await context.MockTickets.FirstOrDefaultAsync(t => t.Id == ticket5Id);
        if (ticket5 == null)
        {
            ticket5 = new MockTicket { Id = ticket5Id };
            await context.MockTickets.AddAsync(ticket5);
        }
        ticket5.OrganizerId = vieOnId;
        ticket5.TicketCode = "ATSH-VIP-886";
        ticket5.EventName = "Anh Trai Say Hi Concert 2026";
        ticket5.SeatZone = "VIP Zone A - Row 3 Seat 04";
        ticket5.OriginalPrice = 2500000;
        ticket5.OwnerEmail = "linhtranlatao2004@gmail.com";
        ticket5.OwnerPhone = "0901234567";
        ticket5.OwnerName = "Nguyen Van Seller";
        ticket5.Status = "VALID";

        // =====================================================================
        // BTC 2: SpaceSpeakers — Mixtape Hanoi 2026
        // =====================================================================
        var ticket6 = await context.MockTickets.FirstOrDefaultAsync(t => t.Id == ticket6Id);
        if (ticket6 == null)
        {
            ticket6 = new MockTicket { Id = ticket6Id };
            await context.MockTickets.AddAsync(ticket6);
        }
        ticket6.OrganizerId = spaceSpeakersId;
        ticket6.TicketCode = "SS-VIP-001";
        ticket6.EventName = "Mixtape Hanoi 2026";
        ticket6.SeatZone = "VIP Lounge A";
        ticket6.OriginalPrice = 3500000;
        ticket6.OwnerEmail = "linhtranlatao2004@gmail.com";
        ticket6.OwnerPhone = "0901234567";
        ticket6.OwnerName = "Nguyen Van Seller";
        ticket6.Status = "VALID";

        var ticket7 = await context.MockTickets.FirstOrDefaultAsync(t => t.Id == ticket7Id);
        if (ticket7 == null)
        {
            ticket7 = new MockTicket { Id = ticket7Id };
            await context.MockTickets.AddAsync(ticket7);
        }
        ticket7.OrganizerId = spaceSpeakersId;
        ticket7.TicketCode = "SS-GA-001";
        ticket7.EventName = "Mixtape Hanoi 2026";
        ticket7.SeatZone = "GA Floor Zone";
        ticket7.OriginalPrice = 1200000;
        ticket7.OwnerEmail = "linhtranlatao2004@gmail.com";
        ticket7.OwnerPhone = "0901234567";
        ticket7.OwnerName = "Nguyen Van Seller";
        ticket7.Status = "VALID";

        var ticket8 = await context.MockTickets.FirstOrDefaultAsync(t => t.Id == ticket8Id);
        if (ticket8 == null)
        {
            ticket8 = new MockTicket { Id = ticket8Id };
            await context.MockTickets.AddAsync(ticket8);
        }
        ticket8.OrganizerId = spaceSpeakersId;
        ticket8.TicketCode = "SS-USED-001";
        ticket8.EventName = "Mixtape Hanoi 2026";
        ticket8.SeatZone = "Standard Zone";
        ticket8.OriginalPrice = 800000;
        ticket8.OwnerEmail = "linhtranlatao2004@gmail.com";
        ticket8.OwnerPhone = "0901234567";
        ticket8.OwnerName = "Nguyen Van Seller";
        ticket8.Status = "USED";

        // =====================================================================
        // BTC 3: Live Nation VN — Westlife Vietnam Tour 2026
        // =====================================================================
        var ticket9 = await context.MockTickets.FirstOrDefaultAsync(t => t.Id == ticket9Id);
        if (ticket9 == null)
        {
            ticket9 = new MockTicket { Id = ticket9Id };
            await context.MockTickets.AddAsync(ticket9);
        }
        ticket9.OrganizerId = liveNationVnId;
        ticket9.TicketCode = "LN-VIP-001";
        ticket9.EventName = "Westlife Vietnam Tour 2026";
        ticket9.SeatZone = "VIP Platinum";
        ticket9.OriginalPrice = 5000000;
        ticket9.OwnerEmail = "linhtranlatao2004@gmail.com";
        ticket9.OwnerPhone = "0901234567";
        ticket9.OwnerName = "Nguyen Van Seller";
        ticket9.Status = "VALID";

        var ticket10 = await context.MockTickets.FirstOrDefaultAsync(t => t.Id == ticket10Id);
        if (ticket10 == null)
        {
            ticket10 = new MockTicket { Id = ticket10Id };
            await context.MockTickets.AddAsync(ticket10);
        }
        ticket10.OrganizerId = liveNationVnId;
        ticket10.TicketCode = "LN-GA-001";
        ticket10.EventName = "Westlife Vietnam Tour 2026";
        ticket10.SeatZone = "GA Standing";
        ticket10.OriginalPrice = 1500000;
        ticket10.OwnerEmail = "linhtranlatao2004@gmail.com";
        ticket10.OwnerPhone = "0901234567";
        ticket10.OwnerName = "Nguyen Van Seller";
        ticket10.Status = "VALID";

        var ticket11 = await context.MockTickets.FirstOrDefaultAsync(t => t.Id == ticket11Id);
        if (ticket11 == null)
        {
            ticket11 = new MockTicket { Id = ticket11Id };
            await context.MockTickets.AddAsync(ticket11);
        }
        ticket11.OrganizerId = liveNationVnId;
        ticket11.TicketCode = "LN-CANCEL-001";
        ticket11.EventName = "Westlife Vietnam Tour 2026";
        ticket11.SeatZone = "Standard Zone";
        ticket11.OriginalPrice = 900000;
        ticket11.OwnerEmail = "linhtranlatao2004@gmail.com";
        ticket11.OwnerPhone = "0901234567";
        ticket11.OwnerName = "Nguyen Van Seller";
        ticket11.Status = "CANCELLED";

        await context.SaveChangesAsync();
    }
}
