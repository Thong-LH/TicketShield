using TicketShield.Application.Features.ResaleListings.Commands.HoldListingForPurchase;
using Xunit;

namespace TicketShield.UnitTests.Features.ResaleListings;

public class HoldListingForPurchaseCommandValidatorTests
{
    private readonly HoldListingForPurchaseCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenListingIdIsEmpty_ShouldHaveValidationError()
    {
        var command = new HoldListingForPurchaseCommand { ListingId = Guid.Empty };
        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.ListingId));
    }

    [Fact]
    public void Validate_WhenSelectedListingIdsIsNull_ShouldBeValid()
    {
        var command = new HoldListingForPurchaseCommand
        {
            ListingId = Guid.NewGuid(),
            SelectedListingIds = null
        };
        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WhenSelectedListingIdsHasDistinctValidIds_ShouldBeValid()
    {
        var command = new HoldListingForPurchaseCommand
        {
            ListingId = Guid.NewGuid(),
            SelectedListingIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() }
        };
        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WhenSelectedListingIdsIsEmptyList_ShouldHaveValidationError()
    {
        var command = new HoldListingForPurchaseCommand
        {
            ListingId = Guid.NewGuid(),
            SelectedListingIds = new List<Guid>()
        };
        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.SelectedListingIds));
    }

    [Fact]
    public void Validate_WhenSelectedListingIdsContainsDuplicates_ShouldHaveValidationError()
    {
        var id = Guid.NewGuid();
        var command = new HoldListingForPurchaseCommand
        {
            ListingId = Guid.NewGuid(),
            SelectedListingIds = new List<Guid> { id, id }
        };
        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.SelectedListingIds));
    }
}
