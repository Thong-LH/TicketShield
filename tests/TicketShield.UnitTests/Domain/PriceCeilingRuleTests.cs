using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Features.ResaleListings.Commands.CreateResaleListing;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using Xunit;

namespace TicketShield.UnitTests.Domain;

/// <summary>
/// SCRUM-36 · TEST-2.5.1 — Price ceiling rule: a resale price may never exceed the original (face) price.
/// The rule is enforced in three places, and each is tested here:
///   1. Domain:    ResaleListing.ValidatePriceCeiling()
///   2. Validator: CreateResaleListingCommandValidator (rejects the request before the handler runs)
///   3. Handler:   CreateResaleListingCommandHandler (must not save a listing that breaks the rule)
/// </summary>
public class PriceCeilingRuleTests
{
    private const string CeilingMessage = "không được vượt quá giá gốc";

    private static decimal Vnd(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    // ───────────────────────── 1. Domain rule ─────────────────────────

    [Theory]
    [InlineData("2500000", "2500000")]      // exactly the original price → allowed (boundary)
    [InlineData("2500000", "2499999")]      // 1 VND below → allowed
    [InlineData("2500000", "1")]            // far below → allowed
    [InlineData("1000000.50", "1000000.50")] // equal with decimals → allowed
    public void ValidatePriceCeiling_WhenResalePriceIsAtOrBelowOriginal_ShouldNotThrow(string original, string resale)
    {
        // Arrange
        var listing = new ResaleListing { OriginalPrice = Vnd(original), ResalePrice = Vnd(resale) };

        // Act
        var exception = Record.Exception(() => listing.ValidatePriceCeiling());

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("2500000", "2500001")]      // 1 VND above → rejected (boundary)
    [InlineData("2500000", "2500000.01")]   // 1 cent above → rejected
    [InlineData("2500000", "5000000")]      // double the price → rejected
    public void ValidatePriceCeiling_WhenResalePriceIsAboveOriginal_ShouldThrowBusinessRuleViolation(string original, string resale)
    {
        // Arrange
        var listing = new ResaleListing { OriginalPrice = Vnd(original), ResalePrice = Vnd(resale) };

        // Act
        var exception = Assert.Throws<BusinessRuleViolationException>(() => listing.ValidatePriceCeiling());

        // Assert
        Assert.Contains(CeilingMessage, exception.Message);
    }

    // ───────────────────────── 2. Request validator ─────────────────────────

    private static CreateResaleListingCommand Command(decimal original, decimal resale) => new()
    {
        EventId = Guid.NewGuid(),
        TierId = Guid.NewGuid(),
        OriginalTicketCode = "TCK-CEILING-001",
        OriginalPrice = original,
        ResalePrice = resale
    };

    [Fact]
    public void Validator_WhenResalePriceEqualsOriginal_ShouldBeValid()
    {
        var result = new CreateResaleListingCommandValidator().Validate(Command(2_500_000m, 2_500_000m));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validator_WhenResalePriceIsOneVndAboveOriginal_ShouldRejectResalePrice()
    {
        var result = new CreateResaleListingCommandValidator().Validate(Command(2_500_000m, 2_500_001m));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e =>
            e.PropertyName == nameof(CreateResaleListingCommand.ResalePrice) && e.ErrorMessage.Contains(CeilingMessage));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validator_WhenResalePriceIsNotPositive_ShouldRejectResalePrice(int resale)
    {
        var result = new CreateResaleListingCommandValidator().Validate(Command(2_500_000m, resale));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateResaleListingCommand.ResalePrice));
    }

    // ───────────────────────── 3. Command handler ─────────────────────────

    private static (TicketShieldDbContext Db, Event Event, TicketTier Tier, User Seller) CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var context = new TicketShieldDbContext(options);

        var organizer = new Organizer { Id = Guid.NewGuid(), Name = "Ceiling Test Organizer", OfficialEmail = "organizer@test.com" };
        var testEvent = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizer.Id,
            Name = "Ceiling Test Concert",
            Venue = "My Dinh Stadium",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(10),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(10).AddHours(4),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(9),
            Organizer = organizer
        };
        var tier = new TicketTier { Id = Guid.NewGuid(), EventId = testEvent.Id, TierName = "VIP", OriginalPrice = 2_500_000m, Event = testEvent };
        var seller = new User { Id = Guid.NewGuid(), Email = "ceiling-seller@ticketshield.vn", FullName = "Ceiling Seller", Role = UserRole.User };

        context.Organizers.Add(organizer);
        context.Events.Add(testEvent);
        context.TicketTiers.Add(tier);
        context.Users.Add(seller);
        context.SaveChanges();
        return (context, testEvent, tier, seller);
    }

    [Fact]
    public async Task Handler_WhenResalePriceIsAboveOriginal_ShouldThrowAndNotSaveListing()
    {
        // Arrange
        var (db, testEvent, tier, seller) = CreateInMemoryDbContext();
        var handler = new CreateResaleListingCommandHandler(db);
        var command = new CreateResaleListingCommand
        {
            EventId = testEvent.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-CEILING-ABOVE",
            OriginalPrice = 2_500_000m,
            ResalePrice = 2_500_001m
        };

        // Act
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => handler.Handle(command, CancellationToken.None));

        // Assert — nothing was written to the database
        Assert.False(await db.ResaleListings.AnyAsync(l => l.OriginalTicketCode == "TCK-CEILING-ABOVE"));
    }

    [Fact]
    public async Task Handler_WhenResalePriceEqualsOriginal_ShouldSaveListing()
    {
        // Arrange
        var (db, testEvent, tier, seller) = CreateInMemoryDbContext();
        var handler = new CreateResaleListingCommandHandler(db);
        var command = new CreateResaleListingCommand
        {
            EventId = testEvent.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = "TCK-CEILING-EQUAL",
            OriginalPrice = 2_500_000m,
            ResalePrice = 2_500_000m
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var saved = await db.ResaleListings.SingleAsync(l => l.OriginalTicketCode == "TCK-CEILING-EQUAL");
        Assert.Equal(saved.OriginalPrice, saved.ResalePrice);
    }
}
