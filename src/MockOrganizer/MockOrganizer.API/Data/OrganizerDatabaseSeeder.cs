using Microsoft.EntityFrameworkCore;
using MockOrganizer.API.Entities;

namespace MockOrganizer.API.Data;

public static class OrganizerDatabaseSeeder
{
    public static async Task SeedOrganizerAsync(OrganizerDbContext context)
    {
        // 1. Auto-apply any pending migrations on startup (code-first auto-init)
        await context.Database.MigrateAsync();

        // 2. Seed Mock Tickets if empty
        if (!await context.MockTickets.AnyAsync())
        {
            var tickets = new List<MockTicket>
            {
                new()
                {
                    Id = Guid.Parse("a0000000-0000-0000-0000-000000000001"),
                    TicketCode = "ATSH-VIP-888",
                    EventName = "Anh Trai Say Hi Concert 2026",
                    SeatZone = "VIP Zone A - Row 1 Seat 12",
                    OriginalPrice = 2500000,
                    OwnerEmail = "linhtranlatao2004@gmail.com",
                    OwnerPhone = "0901234567",
                    OwnerName = "Nguyen Van Seller",
                    Status = "VALID"
                },
                new()
                {
                    Id = Guid.Parse("a0000000-0000-0000-0000-000000000002"),
                    TicketCode = "ATSH-GA-999",
                    EventName = "Anh Trai Say Hi Concert 2026",
                    SeatZone = "GA Standing Zone 2",
                    OriginalPrice = 1200000,
                    OwnerEmail = "linhtranlatao2004@gmail.com",
                    OwnerPhone = "0901234567",
                    OwnerName = "Nguyen Van Seller",
                    Status = "VALID"
                },
                new()
                {
                    Id = Guid.Parse("a0000000-0000-0000-0000-000000000003"),
                    TicketCode = "ATSH-USED-001",
                    EventName = "Anh Trai Say Hi Concert 2026",
                    SeatZone = "Standard Zone C",
                    OriginalPrice = 800000,
                    OwnerEmail = "linhtranlatao2004@gmail.com",
                    OwnerPhone = "0901234567",
                    OwnerName = "Nguyen Van Seller",
                    Status = "USED"
                }
            };

            await context.MockTickets.AddRangeAsync(tickets);
            await context.SaveChangesAsync();
        }
        else
        {
            // Auto update email and reset locked tickets to VALID for testing Gmail OTP
            var existingTickets = await context.MockTickets.ToListAsync();
            foreach (var t in existingTickets)
            {
                if (t.TicketCode != "ATSH-USED-001")
                {
                    t.OwnerEmail = "linhtranlatao2004@gmail.com";
                    t.Status = "VALID";
                }
            }
            await context.SaveChangesAsync();
        }
    }
}
