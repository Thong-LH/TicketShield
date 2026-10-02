using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Features.Organizers.Queries.GetOrganizers;
using TicketShield.Domain.Entities;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.Features.Organizers;

public class GetOrganizersQueryHandlerTests
{
    private static TicketShieldDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new TicketShieldDbContext(options);
    }

    [Fact]
    public async Task Handle_WhenCalled_ShouldReturnOnlyActiveOrganizersSortedByName()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var org1 = new Organizer
        {
            Id = Guid.NewGuid(),
            Name = "VieON Entertainment",
            OfficialEmail = "contact@vieon.vn",
            Status = "ACTIVE"
        };
        var org2 = new Organizer
        {
            Id = Guid.NewGuid(),
            Name = "CTy Âm Nhạc XYZ",
            OfficialEmail = "info@xyz.com",
            Status = "ACTIVE"
        };
        var orgInactive = new Organizer
        {
            Id = Guid.NewGuid(),
            Name = "Deprecated Organizer",
            OfficialEmail = "old@deprecated.com",
            Status = "INACTIVE"
        };

        context.Organizers.AddRange(org1, org2, orgInactive);
        await context.SaveChangesAsync();

        var handler = new GetOrganizersQueryHandler(context);
        var query = new GetOrganizersQuery();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.Equal(2, response.Data.Count);

        // Verify sorted order by name
        Assert.Equal("CTy Âm Nhạc XYZ", response.Data[0].Name);
        Assert.Equal("VieON Entertainment", response.Data[1].Name);
        Assert.Equal("VIEONENTERTAINMENT", response.Data[1].Code);
    }

    [Fact]
    public async Task Handle_WhenNoActiveOrganizers_ShouldReturnEmptyList()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var handler = new GetOrganizersQueryHandler(context);
        var query = new GetOrganizersQuery();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Empty(response.Data);
    }
}
