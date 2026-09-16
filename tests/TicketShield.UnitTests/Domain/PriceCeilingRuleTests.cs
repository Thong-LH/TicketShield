using System.Globalization;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using Xunit;

namespace TicketShield.UnitTests.Domain;

/// <summary>
/// SCRUM-58 · TEST-2.6.1 — Configurable markup price ceiling (BR-G01).
/// Ceiling = Truncate(OriginalPrice × (1 + markupPercent / 100)).
/// Markup 0 must keep the old US-2.5 behaviour (resale may not exceed face value).
/// </summary>
public class PriceCeilingRuleTests
{
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

    [Theory]
    [InlineData("2500000", "0", "2500000")]
    [InlineData("2500000", "10", "2750000")]
    [InlineData("1200000", "0", "1200000")]
    [InlineData("1000000", "10", "1100000")]
    public void ComputePriceCeiling_ShouldTruncateWholeDong(string original, string markup, string expected)
    {
        var ceiling = ResaleListing.ComputePriceCeiling(Vnd(original), Vnd(markup));

        Assert.Equal(Vnd(expected), ceiling);
    }

    [Theory]
    [InlineData("2500000", "2500000")]
    [InlineData("2500000", "2499999")]
    [InlineData("2500000", "1")]
    public void ValidatePriceCeiling_WhenMarkupIsZero_ShouldKeepTheOldFaceValueRule(string original, string resale)
    {
        var listing = Listing(original, resale);

        var exception = Record.Exception(() => listing.ValidatePriceCeiling(0m));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("2500000", "2500001")]
    [InlineData("2500000", "5000000")]
    public void ValidatePriceCeiling_WhenMarkupIsZero_ShouldRejectAboveFaceValue(string original, string resale)
    {
        var listing = Listing(original, resale);

        var exception = Assert.Throws<BusinessRuleViolationException>(() => listing.ValidatePriceCeiling(0m));

        Assert.Contains("trần", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatePriceCeiling_WhenMarkupIsTenPercent_ShouldAllowExactCeiling()
    {
        var listing = Listing("2500000", "2750000");

        var exception = Record.Exception(() => listing.ValidatePriceCeiling(10m));

        Assert.Null(exception);
    }

    [Fact]
    public void ValidatePriceCeiling_WhenMarkupIsTenPercent_ShouldRejectOneDongAboveCeiling()
    {
        var listing = Listing("2500000", "2750001");

        var exception = Assert.Throws<BusinessRuleViolationException>(() => listing.ValidatePriceCeiling(10m));
        var ceiling = ResaleListing.ComputePriceCeiling(listing.OriginalPrice, 10m);

        Assert.Contains(listing.ResalePrice.ToString("N0"), exception.Message);
        Assert.Contains(ceiling.ToString("N0"), exception.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    [InlineData(101)]
    public void ComputePriceCeiling_WhenMarkupIsOutsideZeroToOneHundred_ShouldThrow(decimal markup)
    {
        Assert.Throws<BusinessRuleViolationException>(() => ResaleListing.ComputePriceCeiling(2_500_000m, markup));
    }

    [Fact]
    public void ComputePriceCeiling_WhenOriginalPriceIsZero_ShouldReturnZero()
    {
        Assert.Equal(0m, ResaleListing.ComputePriceCeiling(0m, 10m));
    }

    [Fact]
    public void ValidatePriceCeiling_ShouldOnlyCheck_AndNeverChangeTheListing()
    {
        var listing = Listing("2500000", "2000000");
        listing.ListingStatus = ListingStatus.Verified;

        listing.ValidatePriceCeiling(10m);

        Assert.Equal(Vnd("2500000"), listing.OriginalPrice);
        Assert.Equal(Vnd("2000000"), listing.ResalePrice);
        Assert.Equal(ListingStatus.Verified, listing.ListingStatus);
        Assert.Equal(0m, listing.AppliedMarkupPercentage);
    }

    [Fact]
    public void ValidatePriceCeiling_WhenCalledWithoutArgument_ShouldUseSnapshotOnTheListing()
    {
        var listing = Listing("2500000", "2750000");
        listing.AppliedMarkupPercentage = 10m;

        var exception = Record.Exception(() => listing.ValidatePriceCeiling());

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("2500000", "2500000", "0", "0")]
    [InlineData("2500000", "2000000", "500000", "20.0")]
    [InlineData("2500000", "1250000", "1250000", "50.0")]
    public void Discount_ShouldFollowTheGapBetweenOriginalAndResalePrice(
        string original, string resale, string expectedAmount, string expectedPercentage)
    {
        var listing = Listing(original, resale);

        Assert.Equal(Vnd(expectedAmount), listing.DiscountAmount);
        Assert.Equal(Vnd(expectedPercentage), listing.DiscountPercentage);
    }

    [Fact]
    public void DiscountPercentage_ShouldBeRoundedToOneDecimal()
    {
        var listing = Listing("3000000", "2000000");

        Assert.Equal(33.3m, listing.DiscountPercentage);
    }

    [Fact]
    public void DiscountPercentage_WhenOriginalPriceIsZero_ShouldBeZeroInsteadOfDividingByZero()
    {
        var listing = Listing("0", "0");

        Assert.Equal(0m, listing.DiscountPercentage);
        Assert.Equal(0m, listing.DiscountAmount);
    }
}
