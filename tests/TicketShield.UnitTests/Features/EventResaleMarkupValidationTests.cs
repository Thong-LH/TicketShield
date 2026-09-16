using TicketShield.API.Controllers.Admin;
using TicketShield.Application.Features.Admin.EventMarkup.Commands.UpdateEventResaleMarkup;
using Xunit;

namespace TicketShield.UnitTests.Features;

public class EventResaleMarkupValidationTests
{
    private readonly UpdateEventResaleMarkupCommandValidator _validator = new();

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(100)]
    public void Admin_UpdateEventMarkup_WithinBounds_ShouldPass(decimal markup)
    {
        var command = new UpdateEventResaleMarkupCommand
        {
            EventId = Guid.NewGuid(),
            MaxResaleMarkupPercentage = markup
        };

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    [InlineData(101)]
    public void Admin_UpdateEventMarkup_OutOfBounds_ShouldFail(decimal markup)
    {
        var command = new UpdateEventResaleMarkupCommand
        {
            EventId = Guid.NewGuid(),
            MaxResaleMarkupPercentage = markup
        };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void EventMarkupController_ShouldRequireAdminRole()
    {
        var authorizeAttributes = typeof(EventMarkupController)
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true);

        Assert.NotEmpty(authorizeAttributes);
        var authAttr = (Microsoft.AspNetCore.Authorization.AuthorizeAttribute)authorizeAttributes[0];
        Assert.Equal("Admin", authAttr.Roles);
    }
}
