using System.Globalization;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using Xunit;

namespace TicketShield.UnitTests.Domain;

/// <summary>
/// SCRUM-36 · TEST-2.5.1 — Price ceiling rule: a resale price may never exceed the original
/// (face) price.
///
/// The rule lives in the domain entity as Global Law 1 (ResaleListing.ValidatePriceCeiling),
/// which is what these tests cover, together with the price arithmetic that depends on it.
/// The API-level consequence (publishing above the ceiling is rejected with 422) is covered by
/// the integration test ResaleFlowTests, so it is deliberately not duplicated here.
///
/// Note: the CreateResaleListing MediatR command that an earlier version of this file also
/// exercised was removed by the architecture cleanup on flow/MF_02 as dead code ("Path B"),
/// so the publish path now runs entirely through TicketVerificationService.
/// </summary>
public class PriceCeilingRuleTests
{
    private const string CeilingMessage = "không được vượt quá giá gốc";

    private static decimal Vnd(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    private static ResaleListing Listing(string original, string resale) => new()
    {
        EventId = Guid.NewGuid(),
        TierId = Guid.NewGuid(),
        SellerId = Guid.NewGuid(),
        OriginalTicketCode = "TCK-CEILING-001",
        OriginalPrice = Vnd(original),
        ResalePrice = Vnd(resale)
    };

    // ───────────────────────── 1. The rule itself ─────────────────────────

    [Theory]
    [InlineData("2500000", "2500000")]       // exactly the original price → allowed (boundary)
    [InlineData("2500000", "2499999")]       // 1 VND below → allowed
    [InlineData("2500000", "1")]             // far below → allowed
    [InlineData("1000000.50", "1000000.50")] // equal with decimals → allowed
    public void ValidatePriceCeiling_WhenResalePriceIsAtOrBelowOriginal_ShouldNotThrow(string original, string resale)
    {
        // Arrange
        var listing = Listing(original, resale);

        // Act
        var exception = Record.Exception(() => listing.ValidatePriceCeiling());

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("2500000", "2500001")]     // 1 VND above → rejected (boundary)
    [InlineData("2500000", "2500000.01")]  // 1 cent above → rejected
    [InlineData("2500000", "5000000")]     // double the price → rejected
    public void ValidatePriceCeiling_WhenResalePriceIsAboveOriginal_ShouldThrowBusinessRuleViolation(string original, string resale)
    {
        // Arrange
        var listing = Listing(original, resale);

        // Act
        var exception = Assert.Throws<BusinessRuleViolationException>(() => listing.ValidatePriceCeiling());

        // Assert
        Assert.Contains(CeilingMessage, exception.Message);
    }

    [Fact]
    public void ValidatePriceCeiling_WhenRejecting_ShouldNameBothPricesSoTheSellerCanSeeTheGap()
    {
        // Arrange
        var listing = Listing("2500000", "3000000");

        // Act
        var exception = Assert.Throws<BusinessRuleViolationException>(() => listing.ValidatePriceCeiling());

        // Assert — the seller must be told what they asked for and what the ceiling is
        Assert.Contains(listing.ResalePrice.ToString("N0"), exception.Message);
        Assert.Contains(listing.OriginalPrice.ToString("N0"), exception.Message);
    }

    [Fact]
    public void ValidatePriceCeiling_ShouldOnlyCheck_AndNeverChangeTheListing()
    {
        // Arrange
        var listing = Listing("2500000", "2000000");
        listing.ListingStatus = ListingStatus.Verified;

        // Act
        listing.ValidatePriceCeiling();

        // Assert — validation must not silently clamp the price or move the listing on
        Assert.Equal(Vnd("2500000"), listing.OriginalPrice);
        Assert.Equal(Vnd("2000000"), listing.ResalePrice);
        Assert.Equal(ListingStatus.Verified, listing.ListingStatus);
    }

    // ──────────── 2. What the rule guarantees about the displayed discount ────────────
    // Because the resale price can never exceed the original, a listing can never advertise
    // a negative discount. These cover the arithmetic buyers see on the listing card.

    [Theory]
    [InlineData("2500000", "2500000", "0", "0")]          // at the ceiling → no discount at all
    [InlineData("2500000", "2000000", "500000", "20.0")]  // 20% below
    [InlineData("2500000", "1250000", "1250000", "50.0")] // half price
    public void Discount_ShouldFollowTheGapBetweenOriginalAndResalePrice(
        string original, string resale, string expectedAmount, string expectedPercentage)
    {
        // Arrange
        var listing = Listing(original, resale);

        // Act & Assert
        Assert.Equal(Vnd(expectedAmount), listing.DiscountAmount);
        Assert.Equal(Vnd(expectedPercentage), listing.DiscountPercentage);
    }

    [Fact]
    public void DiscountPercentage_ShouldBeRoundedToOneDecimal()
    {
        // Arrange — 1,000,000 off 3,000,000 is 33.333...%
        var listing = Listing("3000000", "2000000");

        // Act & Assert
        Assert.Equal(33.3m, listing.DiscountPercentage);
    }

    [Fact]
    public void DiscountPercentage_WhenOriginalPriceIsZero_ShouldBeZeroInsteadOfDividingByZero()
    {
        // Arrange
        var listing = Listing("0", "0");

        // Act & Assert
        Assert.Equal(0m, listing.DiscountPercentage);
        Assert.Equal(0m, listing.DiscountAmount);
    }
}
