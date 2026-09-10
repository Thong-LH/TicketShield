using TicketShield.Application.Features.ResaleListings.Commands.CreateResaleListing;
using Xunit;

namespace TicketShield.UnitTests.Features.ResaleListings;

public class CreateResaleListingCommandValidatorTests
{
    private readonly CreateResaleListingCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenPriceExceedsCeiling_ShouldHaveValidationError()
    {
        // Arrange
        var command = new CreateResaleListingCommand
        {
            EventId = Guid.NewGuid(),
            TierId = Guid.NewGuid(),
            OriginalTicketCode = "TCK-12345",
            OriginalPrice = 1_000_000m,
            ResalePrice = 1_500_000m, // Exceeds ceiling
            IsPrivate = false
        };

        // Act
        var result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.ResalePrice) && e.ErrorMessage.Contains("không được vượt quá giá gốc"));
    }

    [Fact]
    public void Validate_WhenPricesAreValid_ShouldPass()
    {
        // Arrange
        var command = new CreateResaleListingCommand
        {
            EventId = Guid.NewGuid(),
            TierId = Guid.NewGuid(),
            OriginalTicketCode = "TCK-12345",
            OriginalPrice = 1_000_000m,
            ResalePrice = 900_000m,
            IsPrivate = true
        };

        // Act
        var result = _validator.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WhenTicketCodeIsEmpty_ShouldFail()
    {
        // Arrange
        var command = new CreateResaleListingCommand
        {
            EventId = Guid.NewGuid(),
            TierId = Guid.NewGuid(),
            OriginalTicketCode = "",
            OriginalPrice = 1_000_000m,
            ResalePrice = 900_000m
        };

        // Act
        var result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.OriginalTicketCode));
    }
}
