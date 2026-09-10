using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TicketShield.Infrastructure.Persistence;

public class TicketShieldDbContextFactory : IDesignTimeDbContextFactory<TicketShieldDbContext>
{
    public TicketShieldDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TicketShieldDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=ticketshield_db;Username=postgres;Password=12345");

        return new TicketShieldDbContext(optionsBuilder.Options);
    }
}
