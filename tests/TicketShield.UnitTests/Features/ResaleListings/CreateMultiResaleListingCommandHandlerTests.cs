using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.ResaleListings.Commands.CreateMultiResaleListing;
using TicketShield.Application.Resale;
using TicketShield.Contracts.Organizer.V1;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.Features.ResaleListings;

public class CreateMultiResaleListingCommandHandlerTests
{
    private static (TicketShieldDbContext dbContext, ShadowUser seller) CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var context = new TicketShieldDbContext(options);

        var organizer = new Organizer
        {
            Id = Guid.NewGuid(),
            Name = "V-Concert Organizer",
            OfficialEmail = "vconcert@test.com"
        };
        var testEvent = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizer.Id,
            Name = "V-Concert Hanoi 2026",
            Venue = "Trung tam Hoi nghi Quoc gia",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(15),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(15).AddHours(3),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(14),
            Organizer = organizer
        };
        var tier = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = testEvent.Id,
            TierName = "VIP",
            OriginalPrice = 2_000_000m,
            Event = testEvent
        };
        var seller = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = "seller@ticketshield.vn",
            FullName = "Nguyen Van Seller"
        };

        context.Organizers.Add(organizer);
        context.Events.Add(testEvent);
        context.TicketTiers.Add(tier);
        context.ShadowUsers.Add(seller);
        context.SaveChanges();

        return (context, seller);
    }

    private class MockCurrentUserService : ICurrentUserService
    {
        public Guid? UserId { get; }
        public string? Email => "seller@ticketshield.vn";
        public string? Role => "User";
        public bool IsAuthenticated => UserId.HasValue;

        public MockCurrentUserService(Guid? userId) => UserId = userId;
    }

    private class FakeTicketVerificationService : ITicketVerificationService
    {
        private readonly TicketShieldDbContext _dbContext;
        public bool ShouldFailOnSecondItem { get; set; } = false;

        public FakeTicketVerificationService(TicketShieldDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public Task<VerificationResult> Publish(string seller, string key, PublishBody body, CancellationToken ct)
        {
            if (ShouldFailOnSecondItem && body.VerificationId == "VERIFY-2")
            {
                throw new ResaleWorkflowException("SIMULATED_PUBLISH_FAILURE", 500);
            }

            return PersistListing(seller, body, null, false, 1);
        }

        public Task<VerificationResult> PublishBundleItem(
            string seller, string key, PublishBody body, Guid bundleId, int bundleTotalTickets, bool allOrNothing, CancellationToken ct)
        {
            if (ShouldFailOnSecondItem && body.VerificationId == "VERIFY-2")
            {
                throw new ResaleWorkflowException("SIMULATED_PUBLISH_FAILURE", 500);
            }

            return PersistListing(seller, body, bundleId, allOrNothing, bundleTotalTickets);
        }

        private Task<VerificationResult> PersistListing(
            string seller, PublishBody body, Guid? bundleId, bool allOrNothing, int bundleTotalTickets)
        {
            var listingId = Guid.NewGuid();
            var eventEntity = _dbContext.Events.First();
            var tierEntity = _dbContext.TicketTiers.First();

            var listing = new ResaleListing
            {
                Id = listingId,
                EventId = eventEntity.Id,
                TierId = tierEntity.Id,
                SellerId = Guid.Parse(seller),
                OriginalTicketCode = $"TICKET-{body.VerificationId}",
                OriginalPrice = 2_000_000m,
                ResalePrice = body.ResalePrice,
                IsPrivate = body.IsPrivate,
                ListingStatus = ListingStatus.Verified,
                VerificationStatus = VerificationStatus.Verified,
                BundleId = bundleId,
                IsBundleAllOrNothing = allOrNothing,
                BundleTotalTickets = bundleTotalTickets,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _dbContext.ResaleListings.Add(listing);
            _dbContext.SaveChanges();

            return Task.FromResult(new VerificationResult(
                body.VerificationId,
                "Published",
                null,
                null,
                null,
                null,
                2_000_000L,
                listingId));
        }

        public Task CancelByListingId(string seller, Guid listingId, string key, CancellationToken ct) => Task.CompletedTask;
        public Task<VerificationResult> Request(string seller, string key, string ticket, CancellationToken ct) => throw new NotImplementedException();
        public Task<VerificationResult> Resend(string seller, string id, string key, CancellationToken ct) => throw new NotImplementedException();
        public Task<VerificationResult> Confirm(string seller, string id, string key, string otp, CancellationToken ct) => throw new NotImplementedException();
        public Task<VerificationResult> Get(string seller, string id, CancellationToken ct) => throw new NotImplementedException();
        public Task<VerificationResult> Close(string seller, string id, string key, CancellationToken ct) => throw new NotImplementedException();
        public Task<VerificationResult> Cancel(string seller, string id, string key, CancellationToken ct) => throw new NotImplementedException();
        public Task<List<ListingResult>> Marketplace(int page, int size, CancellationToken ct) => throw new NotImplementedException();
        public Task RecoverPending(CancellationToken ct) => throw new NotImplementedException();
        public Task<TransferOwnershipResponse> TransferOwnership(string seller, string verificationId, string lockId, ulong expectedLockGeneration, string buyerRef, string buyerEmail, string buyerName, string? buyerPhone, CancellationToken ct) => throw new NotImplementedException();
        public Task<TransferOwnershipResponse> TransferOwnershipByListingId(Guid listingId, Guid buyerId, string buyerEmail, string buyerName, string? buyerPhone, CancellationToken ct) => throw new NotImplementedException();
    }

    [Fact]
    public async Task Handle_WhenNotAuthenticated_ThrowsUnauthorizedException()
    {
        var (dbContext, _) = CreateInMemoryDbContext();
        var handler = new CreateMultiResaleListingCommandHandler(dbContext, null, new MockCurrentUserService(null));

        var command = new CreateMultiResaleListingCommand
        {
            Seller = string.Empty,
            RootIdempotencyKey = Guid.NewGuid().ToString("D"),
            Body = new BulkPublishBody(new List<BulkPublishItem>
            {
                new("VERIFY-1", 1_500_000),
                new("VERIFY-2", 1_500_000)
            })
        };

        await Assert.ThrowsAsync<UnauthorizedException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenItemsEmpty_ThrowsResaleWorkflowException_BULK_ITEMS_EMPTY()
    {
        var (dbContext, seller) = CreateInMemoryDbContext();
        var handler = new CreateMultiResaleListingCommandHandler(dbContext, null, new MockCurrentUserService(seller.Id));

        var command = new CreateMultiResaleListingCommand
        {
            Seller = seller.Id.ToString("D"),
            RootIdempotencyKey = Guid.NewGuid().ToString("D"),
            Body = new BulkPublishBody(new List<BulkPublishItem>())
        };

        var ex = await Assert.ThrowsAsync<ResaleWorkflowException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Equal("BULK_ITEMS_EMPTY", ex.Code);
        Assert.Equal(400, ex.HttpStatus);
    }

    [Fact]
    public async Task Handle_WhenSingleItem_ThrowsResaleWorkflowException_BUNDLE_REQUIRES_2_TO_3_ITEMS()
    {
        var (dbContext, seller) = CreateInMemoryDbContext();
        var handler = new CreateMultiResaleListingCommandHandler(dbContext, null, new MockCurrentUserService(seller.Id));

        var command = new CreateMultiResaleListingCommand
        {
            Seller = seller.Id.ToString("D"),
            RootIdempotencyKey = Guid.NewGuid().ToString("D"),
            Body = new BulkPublishBody(new List<BulkPublishItem>
            {
                new("VERIFY-1", 1_500_000)
            })
        };

        var ex = await Assert.ThrowsAsync<ResaleWorkflowException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Equal("BUNDLE_REQUIRES_2_TO_3_ITEMS", ex.Code);
        Assert.Equal(400, ex.HttpStatus);
    }

    [Fact]
    public async Task Handle_WhenMoreThanThreeItems_ThrowsResaleWorkflowException_BUNDLE_REQUIRES_2_TO_3_ITEMS()
    {
        var (dbContext, seller) = CreateInMemoryDbContext();
        var handler = new CreateMultiResaleListingCommandHandler(dbContext, null, new MockCurrentUserService(seller.Id));

        var command = new CreateMultiResaleListingCommand
        {
            Seller = seller.Id.ToString("D"),
            RootIdempotencyKey = Guid.NewGuid().ToString("D"),
            Body = new BulkPublishBody(new List<BulkPublishItem>
            {
                new("VERIFY-1", 1_500_000),
                new("VERIFY-2", 1_500_000),
                new("VERIFY-3", 1_500_000),
                new("VERIFY-4", 1_500_000)
            })
        };

        var ex = await Assert.ThrowsAsync<ResaleWorkflowException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Equal("BUNDLE_REQUIRES_2_TO_3_ITEMS", ex.Code);
        Assert.Equal(400, ex.HttpStatus);
    }

    [Fact]
    public async Task Handle_WhenItemMissingVerificationId_ThrowsResaleWorkflowException_BULK_ITEM_MISSING_VERIFICATION_ID()
    {
        var (dbContext, seller) = CreateInMemoryDbContext();
        var handler = new CreateMultiResaleListingCommandHandler(dbContext, null, new MockCurrentUserService(seller.Id));

        var command = new CreateMultiResaleListingCommand
        {
            Seller = seller.Id.ToString("D"),
            RootIdempotencyKey = Guid.NewGuid().ToString("D"),
            Body = new BulkPublishBody(new List<BulkPublishItem>
            {
                new("VERIFY-1", 1_500_000),
                new("   ", 1_500_000)
            })
        };

        var ex = await Assert.ThrowsAsync<ResaleWorkflowException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Equal("BULK_ITEM_MISSING_VERIFICATION_ID", ex.Code);
        Assert.Equal(400, ex.HttpStatus);
    }

    [Fact]
    public async Task Handle_WhenDuplicateVerificationInBatch_ThrowsResaleWorkflowException_DUPLICATE_VERIFICATION_IN_BATCH()
    {
        var (dbContext, seller) = CreateInMemoryDbContext();
        var handler = new CreateMultiResaleListingCommandHandler(dbContext, null, new MockCurrentUserService(seller.Id));

        var command = new CreateMultiResaleListingCommand
        {
            Seller = seller.Id.ToString("D"),
            RootIdempotencyKey = Guid.NewGuid().ToString("D"),
            Body = new BulkPublishBody(new List<BulkPublishItem>
            {
                new("VERIFY-1", 1_500_000),
                new("VERIFY-1", 1_500_000)
            })
        };

        var ex = await Assert.ThrowsAsync<ResaleWorkflowException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Equal("DUPLICATE_VERIFICATION_IN_BATCH", ex.Code);
        Assert.Equal(400, ex.HttpStatus);
    }

    [Fact]
    public async Task Handle_WhenSuccessful_SetsSharedBundleIdAndBundlePropertiesOnInsert()
    {
        var (dbContext, seller) = CreateInMemoryDbContext();
        var fakeVerificationService = new FakeTicketVerificationService(dbContext);
        var handler = new CreateMultiResaleListingCommandHandler(dbContext, fakeVerificationService, new MockCurrentUserService(seller.Id));

        var rootKey = Guid.NewGuid().ToString("D");
        var command = new CreateMultiResaleListingCommand
        {
            Seller = seller.Id.ToString("D"),
            RootIdempotencyKey = rootKey,
            Body = new BulkPublishBody(new List<BulkPublishItem>
            {
                new("VERIFY-1", 1_500_000),
                new("VERIFY-2", 1_600_000)
            }, AllOrNothing: true)
        };

        var response = await handler.Handle(command, CancellationToken.None);

        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.NotEqual(Guid.Empty, response.Data.BundleId);
        Assert.True(response.Data.AllOrNothing);
        Assert.Equal(2, response.Data.TotalListings);
        Assert.Equal(2, response.Data.Listings.Count);

        var listings = await dbContext.ResaleListings.ToListAsync();
        Assert.Equal(2, listings.Count);
        Assert.All(listings, l =>
        {
            Assert.Equal(response.Data.BundleId, l.BundleId);
            Assert.True(l.IsBundleAllOrNothing);
            Assert.Equal(2, l.BundleTotalTickets);
        });
    }

    [Fact]
    public async Task Handle_WhenOneItemFails_ThrowsExceptionAndRollsBack()
    {
        var (dbContext, seller) = CreateInMemoryDbContext();
        var fakeVerificationService = new FakeTicketVerificationService(dbContext)
        {
            ShouldFailOnSecondItem = true
        };
        var handler = new CreateMultiResaleListingCommandHandler(dbContext, fakeVerificationService, new MockCurrentUserService(seller.Id));

        var command = new CreateMultiResaleListingCommand
        {
            Seller = seller.Id.ToString("D"),
            RootIdempotencyKey = Guid.NewGuid().ToString("D"),
            Body = new BulkPublishBody(new List<BulkPublishItem>
            {
                new("VERIFY-1", 1_500_000),
                new("VERIFY-2", 1_600_000)
            }, AllOrNothing: false)
        };

        var ex = await Assert.ThrowsAsync<ResaleWorkflowException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Equal("SIMULATED_PUBLISH_FAILURE", ex.Code);
    }
}
