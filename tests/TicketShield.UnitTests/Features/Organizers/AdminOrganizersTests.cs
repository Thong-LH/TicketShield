using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.Admin.Organizers.Commands.CreateOrganizer;
using TicketShield.Application.Features.Admin.Organizers.Commands.SyncOrganizerEvents;
using TicketShield.Application.Features.Admin.Organizers.Commands.UpdateOrganizerConnection;
using TicketShield.Application.Features.Admin.Organizers.Queries.GetAdminOrganizerDetail;
using TicketShield.Application.Features.Admin.Organizers.Queries.GetAdminOrganizers;
using TicketShield.Contracts.Organizer.V1;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.ExternalServices.Organizer;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.Features.Organizers;

public class AdminOrganizersTests
{
    private static TicketShieldDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new TicketShieldDbContext(options);
    }

    [Fact]
    public async Task GetAdminOrganizers_ShouldReturnAllOrganizersWithDetails()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var org = new Organizer
        {
            Id = Guid.NewGuid(),
            Name = "Ticketbox Partner",
            OfficialEmail = "partner@ticketbox.vn",
            Status = "ACTIVE",
            GrpcEndpoint = "http://localhost:50052",
            ConnectionStatus = "CONNECTED"
        };
        context.Organizers.Add(org);
        await context.SaveChangesAsync();

        var handler = new GetAdminOrganizersQueryHandler(context);

        // Act
        var result = await handler.Handle(new GetAdminOrganizersQuery(), CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data);
        Assert.Equal("Ticketbox Partner", result.Data[0].Name);
        Assert.Equal("http://localhost:50052", result.Data[0].GrpcEndpoint);
        Assert.Equal("CONNECTED", result.Data[0].ConnectionStatus);
    }

    [Fact]
    public async Task CreateOrganizer_WithValidData_ShouldSucceed()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var handler = new CreateOrganizerCommandHandler(context);
        var command = new CreateOrganizerCommand
        {
            Name = "New Organizer Hub",
            OfficialEmail = "hub@organizer.io",
            ContactPhone = "0909123456",
            ApiBaseUrl = "https://hub.organizer.io/api",
            GrpcEndpoint = "http://hub.organizer.io:50051",
            WebhookUrl = "https://hub.organizer.io/webhooks/resale",
            WebhookSecretKey = "whsec_supersecretkey"
        };

        // Act
        var response = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.Equal("New Organizer Hub", response.Data.Name);
        Assert.Equal("hub@organizer.io", response.Data.OfficialEmail);
        Assert.Equal("CONNECTED", response.Data.ConnectionStatus);

        // Verify entity in DB
        var savedOrg = await context.Organizers.FirstOrDefaultAsync(o => o.OfficialEmail == "hub@organizer.io");
        Assert.NotNull(savedOrg);
        Assert.False(string.IsNullOrWhiteSpace(savedOrg.ApiKeyHash));
        Assert.Equal("whsec_supersecretkey", savedOrg.WebhookSecretKey);
    }

    [Fact]
    public async Task CreateOrganizer_WithDuplicateEmail_ShouldThrowBadRequestException()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        context.Organizers.Add(new Organizer
        {
            Id = Guid.NewGuid(),
            Name = "Existing Partner",
            OfficialEmail = "partner@existing.com",
            Status = "ACTIVE"
        });
        await context.SaveChangesAsync();

        var handler = new CreateOrganizerCommandHandler(context);
        var command = new CreateOrganizerCommand
        {
            Name = "Another Partner",
            OfficialEmail = "partner@existing.com"
        };

        // Act & Assert
        await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateOrganizerConnection_WhenOrganizerExists_ShouldUpdateEndpoints()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var orgId = Guid.NewGuid();
        context.Organizers.Add(new Organizer
        {
            Id = orgId,
            Name = "VieON Org",
            OfficialEmail = "vieon@test.com",
            Status = "ACTIVE",
            ConnectionStatus = "PENDING"
        });
        await context.SaveChangesAsync();

        var gatewayResolverMock = new Mock<IOrganizerGatewayResolver>();
        var handler = new UpdateOrganizerConnectionCommandHandler(context, gatewayResolverMock.Object);
        var command = new UpdateOrganizerConnectionCommand
        {
            Id = orgId,
            Name = "VieON Group Updated",
            ContactPhone = "0988776655",
            ApiBaseUrl = "https://api.vieon.vn",
            GrpcEndpoint = "http://grpc.vieon.vn:50051",
            WebhookUrl = "https://api.vieon.vn/wh",
            WebhookSecretKey = "whsec_newkey",
            Status = "ACTIVE",
            ConnectionStatus = "CONNECTED"
        };

        // Act
        var response = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.Equal("VieON Group Updated", response.Data.Name);
        Assert.Equal("http://grpc.vieon.vn:50051", response.Data.GrpcEndpoint);
        Assert.Equal("CONNECTED", response.Data.ConnectionStatus);

        var updatedOrg = await context.Organizers.FindAsync(orgId);
        Assert.NotNull(updatedOrg);
        Assert.Equal("CONNECTED", updatedOrg.ConnectionStatus);
    }

    [Fact]
    public async Task UpdateOrganizerConnection_WhenNotFound_ShouldThrowNotFoundException()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var gatewayResolverMock = new Mock<IOrganizerGatewayResolver>();
        var handler = new UpdateOrganizerConnectionCommandHandler(context, gatewayResolverMock.Object);
        var command = new UpdateOrganizerConnectionCommand
        {
            Id = Guid.NewGuid(),
            Name = "Non Existent",
            Status = "ACTIVE",
            ConnectionStatus = "CONNECTED"
        };

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task OrganizerGatewayResolver_WhenNullOrganizerId_ShouldFallbackToDefaultClient()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var defaultChannel = GrpcChannel.ForAddress("http://127.0.0.1:50051");
        var defaultClient = new OrganizerResaleService.OrganizerResaleServiceClient(defaultChannel);

        var defaultOptions = new OrganizerConnectionOptions
        {
            Address = "http://127.0.0.1:50051",
            ApiKey = "test_key",
            DeadlineSeconds = 30
        };

        var resolver = new OrganizerGatewayResolver(
            context,
            defaultOptions,
            defaultClient,
            NullLogger<OrganizerGatewayResolver>.Instance);

        // Act - null organizerId fallback
        var client = await resolver.ResolveClientAsync(null, CancellationToken.None);

        // Assert
        Assert.NotNull(client);
        Assert.Same(defaultClient, client);
    }

    [Fact]
    public async Task OrganizerGatewayResolver_WhenOrganizerIdProvided_ShouldResolveSpecificEndpoint()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var orgId = Guid.NewGuid();
        context.Organizers.Add(new Organizer
        {
            Id = orgId,
            Name = "Dynamic Org",
            OfficialEmail = "dynamic@test.com",
            GrpcEndpoint = "http://127.0.0.1:50060",
            ConnectionStatus = "CONNECTED",
            Status = "ACTIVE"
        });
        await context.SaveChangesAsync();

        var defaultChannel = GrpcChannel.ForAddress("http://127.0.0.1:50051");
        var defaultClient = new OrganizerResaleService.OrganizerResaleServiceClient(defaultChannel);

        var defaultOptions = new OrganizerConnectionOptions
        {
            Address = "http://127.0.0.1:50051",
            ApiKey = "test_key",
            DeadlineSeconds = 30
        };

        var resolver = new OrganizerGatewayResolver(
            context,
            defaultOptions,
            defaultClient,
            NullLogger<OrganizerGatewayResolver>.Instance);

        // Act
        var client = await resolver.ResolveClientAsync(orgId, CancellationToken.None);

        // Assert
        Assert.NotNull(client);
        Assert.NotSame(defaultClient, client);
    }

    [Fact]
    public async Task SyncOrganizerEvents_WhenOrganizerExists_ShouldUpsertEventsAndTiersAndSetStatusActive()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var orgId = Guid.NewGuid();
        context.Organizers.Add(new Organizer
        {
            Id = orgId,
            Name = "SpaceSpeakers Production",
            OfficialEmail = "spacespeakers@test.com",
            Status = "DRAFT",
            ConnectionStatus = "CONNECTED"
        });
        await context.SaveChangesAsync();

        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient());

        var handler = new SyncOrganizerEventsCommandHandler(
            context,
            httpClientFactoryMock.Object,
            NullLogger<SyncOrganizerEventsCommandHandler>.Instance);

        var command = new SyncOrganizerEventsCommand(orgId);

        // Act
        var response = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.True(response.Data.EventsCount > 0);
        Assert.True(response.Data.TiersCount > 0);

        var updatedOrg = await context.Organizers
            .Include(o => o.Events)
            .ThenInclude(e => e.TicketTiers)
            .FirstOrDefaultAsync(o => o.Id == orgId);

        Assert.NotNull(updatedOrg);
        Assert.Equal("ACTIVE", updatedOrg.Status);
        Assert.NotNull(updatedOrg.LastCatalogSyncedAt);
        Assert.NotEmpty(updatedOrg.Events);
    }

    [Fact]
    public async Task SyncOrganizerEvents_WhenNotFound_ShouldThrowNotFoundException()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        var handler = new SyncOrganizerEventsCommandHandler(
            context,
            httpClientFactoryMock.Object,
            NullLogger<SyncOrganizerEventsCommandHandler>.Instance);

        var command = new SyncOrganizerEventsCommand(Guid.NewGuid());

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(command, CancellationToken.None));
    }
}
