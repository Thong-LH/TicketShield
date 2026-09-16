using TicketShield.Domain.Entities;
using TicketShield.Domain.Exceptions;
using Xunit;

namespace TicketShield.UnitTests.Domain;

public class ResaleListingDomainTests
{
    [Fact]
    public void ValidatePriceCeiling_WhenResalePriceLowerThanOriginalPrice_ShouldPass()
    {
        // Arrange
        var listing = new ResaleListing
        {
            OriginalPrice = 1_500_000m,
            ResalePrice = 1_200_000m
        };

        // Act & Assert
        listing.ValidatePriceCeiling(0m);
    }

    [Fact]
    public void ValidatePriceCeiling_WhenResalePriceEqualsOriginalPrice_ShouldPass()
    {
        // Arrange
        var listing = new ResaleListing
        {
            OriginalPrice = 1_500_000m,
            ResalePrice = 1_500_000m
        };

        // Act & Assert
        listing.ValidatePriceCeiling(0m);
    }

    [Fact]
    public void ValidatePriceCeiling_WhenResalePriceExceedsOriginalPrice_ShouldThrowBusinessRuleViolationException()
    {
        // Arrange
        var listing = new ResaleListing
        {
            OriginalPrice = 1_500_000m,
            ResalePrice = 1_800_000m
        };

        // Act & Assert
        var exception = Assert.Throws<BusinessRuleViolationException>(() => listing.ValidatePriceCeiling(0m));
        Assert.Contains("không được vượt quá", exception.Message);
    }
}
