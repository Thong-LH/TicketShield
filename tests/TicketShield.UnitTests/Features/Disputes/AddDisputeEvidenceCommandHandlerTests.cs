using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TicketShield.API.Middlewares;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.Disputes.Commands.AddDisputeEvidence;
using TicketShield.Application.Features.Disputes.Queries.GetDisputeEvidenceFile;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Services;

namespace TicketShield.UnitTests.Features.Disputes;

public class AddDisputeEvidenceCommandHandlerTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0x00];

    [Fact]
    public async Task Handle_WhenImageIsValid_StoresOneImageRowWithoutTheOriginalName()
    {
        await using var db = CreateDb();
        var seeded = await SeedDisputeAsync(db);
        using var files = new TempEvidenceStore();
        var handler = new AddDisputeEvidenceCommandHandler(db, files.Store, new StubUser(seeded.BuyerId));

        var result = await handler.Handle(new AddDisputeEvidenceCommand
        {
            DisputeId = seeded.DisputeId,
            Content = Png,
            ContentType = "image/png"
        }, CancellationToken.None);

        db.ChangeTracker.Clear();
        var evidence = await db.DisputeEvidences.AsNoTracking().SingleAsync();
        Assert.Equal("IMAGE", evidence.EvidenceType);
        Assert.Equal(seeded.DisputeId, evidence.DisputeId);
        Assert.Equal(seeded.BuyerId, evidence.UploaderId);
        Assert.Equal(evidence.Id, result.Data!.EvidenceId);
        Assert.Matches("^[0-9a-f]{32}\\.png$", evidence.FileUrl);
        Assert.DoesNotContain("bien-ban", evidence.FileUrl);
        Assert.DoesNotContain(Path.DirectorySeparatorChar, evidence.FileUrl);
        Assert.Equal(Png, await File.ReadAllBytesAsync(Path.Combine(files.Root, evidence.FileUrl)));
    }

    [Fact]
    public async Task Handle_WhenBuyerUploadsTwice_StoresTwoRows()
    {
        await using var db = CreateDb();
        var seeded = await SeedDisputeAsync(db);
        using var files = new TempEvidenceStore();
        var handler = new AddDisputeEvidenceCommandHandler(db, files.Store, new StubUser(seeded.BuyerId));
        var command = new AddDisputeEvidenceCommand
        {
            DisputeId = seeded.DisputeId,
            Content = Png,
            ContentType = "image/png"
        };

        await handler.Handle(command, CancellationToken.None);
        await handler.Handle(command, CancellationToken.None);

        Assert.Equal(2, await db.DisputeEvidences.CountAsync());
    }

    [Fact]
    public async Task Handle_WhenCallerIsNotTheBuyer_Returns403AndStoresNothing()
    {
        await using var db = CreateDb();
        var seeded = await SeedDisputeAsync(db);
        using var files = new TempEvidenceStore();
        var handler = new AddDisputeEvidenceCommandHandler(db, files.Store, new StubUser(Guid.NewGuid()));

        var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() => handler.Handle(new AddDisputeEvidenceCommand
        {
            DisputeId = seeded.DisputeId,
            Content = Png,
            ContentType = "image/png"
        }, CancellationToken.None));

        Assert.Equal(403, await StatusCodeOf(ex));
        Assert.Empty(await db.DisputeEvidences.AsNoTracking().ToListAsync());
        Assert.Empty(Directory.GetFiles(files.Root));
    }

    [Theory]
    [InlineData("text/plain")]
    public async Task Handle_WhenFileIsNotAnImage_Returns400(string contentType)
    {
        await using var db = CreateDb();
        var seeded = await SeedDisputeAsync(db);
        using var files = new TempEvidenceStore();
        var handler = new AddDisputeEvidenceCommandHandler(db, files.Store, new StubUser(seeded.BuyerId));

        var ex = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(new AddDisputeEvidenceCommand
        {
            DisputeId = seeded.DisputeId,
            Content = System.Text.Encoding.UTF8.GetBytes("not-an-image"),
            ContentType = contentType
        }, CancellationToken.None));

        Assert.Equal(400, await StatusCodeOf(ex));
        Assert.Empty(await db.DisputeEvidences.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Handle_WhenMagicBytesDoNotMatch_Returns400()
    {
        await using var db = CreateDb();
        var seeded = await SeedDisputeAsync(db);
        using var files = new TempEvidenceStore();
        var handler = new AddDisputeEvidenceCommandHandler(db, files.Store, new StubUser(seeded.BuyerId));

        var ex = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(new AddDisputeEvidenceCommand
        {
            DisputeId = seeded.DisputeId,
            Content = Jpeg,
            ContentType = "image/png"
        }, CancellationToken.None));

        Assert.Equal(400, await StatusCodeOf(ex));
        Assert.Empty(await db.DisputeEvidences.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Handle_WhenImageIsLargerThanFiveMegabytes_Returns400()
    {
        await using var db = CreateDb();
        var seeded = await SeedDisputeAsync(db);
        using var files = new TempEvidenceStore();
        var handler = new AddDisputeEvidenceCommandHandler(db, files.Store, new StubUser(seeded.BuyerId));
        var tooLarge = new byte[(5 * 1024 * 1024) + 1];
        tooLarge[0] = 0xFF;
        tooLarge[1] = 0xD8;
        tooLarge[2] = 0xFF;

        var ex = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(new AddDisputeEvidenceCommand
        {
            DisputeId = seeded.DisputeId,
            Content = tooLarge,
            ContentType = "image/jpeg"
        }, CancellationToken.None));

        Assert.Equal(400, await StatusCodeOf(ex));
        Assert.Empty(await db.DisputeEvidences.AsNoTracking().ToListAsync());
        Assert.Empty(Directory.GetFiles(files.Root));
    }

    [Fact]
    public async Task Handle_WhenSavingTheRowFails_DeletesTheStoredFile()
    {
        await using var db = CreateFailingDb();
        var seeded = await SeedDisputeAsync(db);
        using var files = new TempEvidenceStore();
        var handler = new AddDisputeEvidenceCommandHandler(db, files.Store, new StubUser(seeded.BuyerId));

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new AddDisputeEvidenceCommand
        {
            DisputeId = seeded.DisputeId,
            Content = Png,
            ContentType = "image/png"
        }, CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.Empty(await db.DisputeEvidences.AsNoTracking().ToListAsync());
        Assert.Empty(Directory.GetFiles(files.Root));
    }

    [Fact]
    public async Task Handle_WhenBuyerReadsTheImage_ReturnsTheBytes()
    {
        await using var db = CreateDb();
        var seeded = await SeedDisputeAsync(db);
        using var files = new TempEvidenceStore();
        var buyer = new StubUser(seeded.BuyerId);
        var added = await new AddDisputeEvidenceCommandHandler(db, files.Store, buyer).Handle(new AddDisputeEvidenceCommand
        {
            DisputeId = seeded.DisputeId,
            Content = Png,
            ContentType = "image/png"
        }, CancellationToken.None);

        var file = await new GetDisputeEvidenceFileQueryHandler(db, files.Store, buyer).Handle(new GetDisputeEvidenceFileQuery
        {
            DisputeId = seeded.DisputeId,
            EvidenceId = added.Data!.EvidenceId
        }, CancellationToken.None);

        Assert.Equal(Png, file.Content);
        Assert.Equal("image/png", file.ContentType);
    }

    [Fact]
    public async Task Handle_WhenSomeoneElseReadsTheImage_Returns403()
    {
        await using var db = CreateDb();
        var seeded = await SeedDisputeAsync(db);
        using var files = new TempEvidenceStore();
        var added = await new AddDisputeEvidenceCommandHandler(db, files.Store, new StubUser(seeded.BuyerId)).Handle(
            new AddDisputeEvidenceCommand
            {
                DisputeId = seeded.DisputeId,
                Content = Png,
                ContentType = "image/png"
            }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            new GetDisputeEvidenceFileQueryHandler(db, files.Store, new StubUser(Guid.NewGuid())).Handle(
                new GetDisputeEvidenceFileQuery
                {
                    DisputeId = seeded.DisputeId,
                    EvidenceId = added.Data!.EvidenceId
                }, CancellationToken.None));

        Assert.Equal(403, await StatusCodeOf(ex));
    }

    private static async Task<int> StatusCodeOf(Exception exception)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw exception,
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        return context.Response.StatusCode;
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

    private static TicketShieldDbContext CreateFailingDb()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>().UseSqlite(connection).Options;
        var db = new EvidenceInsertFailingContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static async Task<(Guid DisputeId, Guid BuyerId)> SeedDisputeAsync(TicketShieldDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var organizer = new Organizer { Id = Guid.NewGuid(), Name = "Organizer", OfficialEmail = $"org-{suffix}@test.local" };
        var seller = new ShadowUser { Id = Guid.NewGuid(), Email = $"seller-{suffix}@test.local", FullName = "Seller" };
        var buyer = new ShadowUser { Id = Guid.NewGuid(), Email = $"buyer-{suffix}@test.local", FullName = "Buyer" };
        var concert = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizer.Id,
            Name = "Concert",
            Venue = "Hall",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(10),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(10).AddHours(2),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(9)
        };
        var tier = new TicketTier { Id = Guid.NewGuid(), EventId = concert.Id, TierName = "GA", OriginalPrice = 100_000m };
        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = concert.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = $"TCK-{suffix}",
            OriginalPrice = 100_000m,
            ResalePrice = 100_000m,
            ListingStatus = ListingStatus.Sold
        };
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer.Id,
            SellerId = seller.Id,
            Status = EscrowStatus.Disputed
        };
        var dispute = new Dispute
        {
            Id = Guid.NewGuid(),
            EscrowId = escrow.Id,
            BuyerId = buyer.Id,
            DisputeCode = $"DP-{suffix}",
            ReasonCode = DisputeReasonCode.TicketInvalid,
            Description = "Vé sai",
            Status = DisputeStatus.Open
        };

        db.Organizers.Add(organizer);
        db.ShadowUsers.AddRange(seller, buyer);
        db.Events.Add(concert);
        db.TicketTiers.Add(tier);
        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(escrow);
        db.Disputes.Add(dispute);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (dispute.Id, buyer.Id);
    }

    private sealed class EvidenceInsertFailingContext : TicketShieldDbContext
    {
        public EvidenceInsertFailingContext(DbContextOptions<TicketShieldDbContext> options) : base(options)
        {
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ChangeTracker.Entries<DisputeEvidence>().Any(entry => entry.State == EntityState.Added))
            {
                throw new InvalidOperationException("evidence save failed");
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class TempEvidenceStore : IDisposable
    {
        public TempEvidenceStore()
        {
            Root = Path.Combine(Path.GetTempPath(), "ts-evidence-" + Guid.NewGuid().ToString("N"));
            Store = new DisputeEvidenceFileStore(Root);
        }

        public string Root { get; }
        public IDisputeEvidenceFileStore Store { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class StubUser(Guid? userId) : ICurrentUserService
    {
        public Guid? UserId { get; } = userId;
        public string? Email => "buyer@test.local";
        public string? Role => "User";
        public bool IsAuthenticated => UserId.HasValue;
    }
}
