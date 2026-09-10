using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MockOrganizer.API.Data;

public class OrganizerDbContextFactory : IDesignTimeDbContextFactory<OrganizerDbContext>
{
    public OrganizerDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<OrganizerDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=organizer_db;Username=postgres;Password=12345");

        return new OrganizerDbContext(optionsBuilder.Options);
    }
}
