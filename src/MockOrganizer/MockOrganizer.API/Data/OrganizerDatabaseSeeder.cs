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
        var ticket1Id = Guid.Parse("a0000000-0000-0000-0000-000000000001");
        var ticket2Id = Guid.Parse("a0000000-0000-0000-0000-000000000002");
        var ticket3Id = Guid.Parse("a0000000-0000-0000-0000-000000000003");
        var seedIds = new HashSet<Guid> { ticket1Id, ticket2Id, ticket3Id };

        // Clean operational logs & OTPs
        context.MockOtps.RemoveRange(await context.MockOtps.ToListAsync());
        context.GateAccessLogs.RemoveRange(await context.GateAccessLogs.ToListAsync());

        // Remove any non-seed tickets
        var extraTickets = await context.MockTickets.Where(t => !seedIds.Contains(t.Id)).ToListAsync();
        if (extraTickets.Any())
        {
            context.MockTickets.RemoveRange(extraTickets);
        }

        // Upsert / Reset Seed Tickets
        var ticket1 = await context.MockTickets.FirstOrDefaultAsync(t => t.Id == ticket1Id);
        if (ticket1 == null)
        {
            ticket1 = new MockTicket { Id = ticket1Id };
            await context.MockTickets.AddAsync(ticket1);
        }
        ticket1.TicketCode = "ATSH-VIP-888";
        ticket1.EventName = "Anh Trai Say Hi Concert 2026";
        ticket1.SeatZone = "VIP Zone A - Row 1 Seat 12";
        ticket1.OriginalPrice = 2500000;
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
        ticket2.TicketCode = "ATSH-GA-999";
        ticket2.EventName = "Anh Trai Say Hi Concert 2026";
        ticket2.SeatZone = "GA Standing Zone 2";
        ticket2.OriginalPrice = 1200000;
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
        ticket3.TicketCode = "ATSH-USED-001";
        ticket3.EventName = "Anh Trai Say Hi Concert 2026";
        ticket3.SeatZone = "Standard Zone C";
        ticket3.OriginalPrice = 800000;
        ticket3.OwnerEmail = "linhtranlatao2004@gmail.com";
        ticket3.OwnerPhone = "0901234567";
        ticket3.OwnerName = "Nguyen Van Seller";
        ticket3.Status = "USED";

        await context.SaveChangesAsync();
    }
}
