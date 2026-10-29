using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TicketShield.API.Controllers;
using TicketShield.Application.Common.Configurations;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.ResaleListings.Commands.ProcessSePayWebhook;
using TicketShield.Application.Features.ResaleListings.Commands.ProcessTicketUsedWebhook;
using Xunit;

namespace TicketShield.UnitTests.QualityAssurance.MF03;

/// <summary>
/// US-3.6_MF03: QA Suite - Bảo mật webhook (Webhook Security & Integrity Testing)
/// Kiểm thử tự động toàn diện: Xác thực Authorization header, API Key, Bearer token,
/// Organizer secret, các biến thể tiền tố, và ngăn chặn truy cập trái phép.
/// </summary>
public class WebhookSecurityQATests
{
    private const string SePayValidSecret = "SePaySecret_QA_Testing_Token_2026";
    private const string OrganizerValidSecret = "OrganizerSecret_QA_Testing_Token_2026";

    private static (WebhooksController controller, Mock<ISender> mediatorMock) CreateSePayController()
    {
        var options = Options.Create(new VietQrSettings
        {
            WebhookSecret = SePayValidSecret
        });

        var mediatorMock = new Mock<ISender>();
        mediatorMock.Setup(m => m.Send(It.IsAny<ProcessSePayWebhookCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<ProcessSePayWebhookResponse>.SuccessResponse(
                new ProcessSePayWebhookResponse
                {
                    EscrowStatus = "Locked",
                    IsIdempotentDuplicate = false
                },
                "Webhook processed successfully"));

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(sp => sp.GetService(typeof(ISender))).Returns(mediatorMock.Object);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider.Object
        };

        var controller = new WebhooksController(options, NullLogger<WebhooksController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        return (controller, mediatorMock);
    }

    private static (OrganizerWebhooksController controller, Mock<ISender> mediatorMock) CreateOrganizerController()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "OrganizerWebhook:Secret", OrganizerValidSecret }
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var mediatorMock = new Mock<ISender>();
        mediatorMock.Setup(m => m.Send(It.IsAny<ProcessTicketUsedWebhookCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<ProcessTicketUsedWebhookResponse>.SuccessResponse(
                new ProcessTicketUsedWebhookResponse
                {
                    EscrowId = Guid.NewGuid(),
                    EscrowStatus = "Releasing"
                },
                "Organizer webhook processed successfully"));

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(sp => sp.GetService(typeof(ISender))).Returns(mediatorMock.Object);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider.Object
        };

        var controller = new OrganizerWebhooksController(NullLogger<OrganizerWebhooksController>.Instance, configuration)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        return (controller, mediatorMock);
    }

    [Fact]
    public async Task SePayWebhook_MissingAuthorizationHeader_Returns401Unauthorized()
    {
        // Arrange
        var (controller, _) = CreateSePayController();
        var payload = new SePayWebhookRequest { Id = 1, TransferType = "in", TransferAmount = 100_000 };

        // Act
        var result = await controller.HandleSePayWebhook(payload);

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorizedResult.StatusCode);
        var apiResponse = Assert.IsType<ApiResponse<object>>(unauthorizedResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Contains("Thiếu header Authorization", apiResponse.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SePayWebhook_EmptyOrWhitespaceAuthorizationHeader_Returns401Unauthorized(string authHeader)
    {
        // Arrange
        var (controller, _) = CreateSePayController();
        controller.HttpContext.Request.Headers["Authorization"] = authHeader;
        var payload = new SePayWebhookRequest { Id = 1, TransferType = "in", TransferAmount = 100_000 };

        // Act
        var result = await controller.HandleSePayWebhook(payload);

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorizedResult.StatusCode);
    }

    [Fact]
    public async Task SePayWebhook_InvalidSecretToken_Returns401Unauthorized()
    {
        // Arrange
        var (controller, _) = CreateSePayController();
        controller.HttpContext.Request.Headers["Authorization"] = "Bearer Inva1id-SePay-Secr3t";
        var payload = new SePayWebhookRequest { Id = 1, TransferType = "in", TransferAmount = 100_000 };

        // Act
        var result = await controller.HandleSePayWebhook(payload);

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorizedResult.StatusCode);
        var apiResponse = Assert.IsType<ApiResponse<object>>(unauthorizedResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Contains("không hợp lệ", apiResponse.Message);
    }

    [Fact]
    public async Task SePayWebhook_ValidRawToken_Returns200OK()
    {
        // Arrange
        var (controller, mediatorMock) = CreateSePayController();
        controller.HttpContext.Request.Headers["Authorization"] = SePayValidSecret;
        var payload = new SePayWebhookRequest { Id = 1, TransferType = "in", TransferAmount = 100_000 };

        // Act
        var result = await controller.HandleSePayWebhook(payload);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);
        mediatorMock.Verify(m => m.Send(It.IsAny<ProcessSePayWebhookCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Apikey " + SePayValidSecret)]
    [InlineData("apikey " + SePayValidSecret)]
    [InlineData("APIKEY " + SePayValidSecret)]
    public async Task SePayWebhook_ValidApiKeyPrefix_Returns200OK(string authHeader)
    {
        // Arrange
        var (controller, mediatorMock) = CreateSePayController();
        controller.HttpContext.Request.Headers["Authorization"] = authHeader;
        var payload = new SePayWebhookRequest { Id = 1, TransferType = "in", TransferAmount = 100_000 };

        // Act
        var result = await controller.HandleSePayWebhook(payload);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);
        mediatorMock.Verify(m => m.Send(It.IsAny<ProcessSePayWebhookCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Bearer " + SePayValidSecret)]
    [InlineData("bearer " + SePayValidSecret)]
    [InlineData("BEARER " + SePayValidSecret)]
    public async Task SePayWebhook_ValidBearerPrefix_Returns200OK(string authHeader)
    {
        // Arrange
        var (controller, mediatorMock) = CreateSePayController();
        controller.HttpContext.Request.Headers["Authorization"] = authHeader;
        var payload = new SePayWebhookRequest { Id = 1, TransferType = "in", TransferAmount = 100_000 };

        // Act
        var result = await controller.HandleSePayWebhook(payload);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);
        mediatorMock.Verify(m => m.Send(It.IsAny<ProcessSePayWebhookCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OrganizerWebhook_MissingSecret_Returns401Unauthorized()
    {
        // Arrange
        var (controller, _) = CreateOrganizerController();
        var payload = new OrganizerTicketUsedWebhookRequest { TicketCode = "TCK-QA-001" };

        // Act
        var result = await controller.HandleTicketUsedWebhook(payload);

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorizedResult.StatusCode);
        var apiResponse = Assert.IsType<ApiResponse<object>>(unauthorizedResult.Value);
        Assert.Contains("không hợp lệ", apiResponse.Message);
    }

    [Fact]
    public async Task OrganizerWebhook_InvalidSecret_Returns401Unauthorized()
    {
        // Arrange
        var (controller, _) = CreateOrganizerController();
        controller.HttpContext.Request.Headers["X-Organizer-Secret"] = "WrongSecret999";
        var payload = new OrganizerTicketUsedWebhookRequest { TicketCode = "TCK-QA-001" };

        // Act
        var result = await controller.HandleTicketUsedWebhook(payload);

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorizedResult.StatusCode);
    }

    [Fact]
    public async Task OrganizerWebhook_ValidXOrganizerSecret_Returns200OK()
    {
        // Arrange
        var (controller, mediatorMock) = CreateOrganizerController();
        controller.HttpContext.Request.Headers["X-Organizer-Secret"] = OrganizerValidSecret;
        var payload = new OrganizerTicketUsedWebhookRequest { TicketCode = "TCK-QA-001" };

        // Act
        var result = await controller.HandleTicketUsedWebhook(payload);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);
        mediatorMock.Verify(m => m.Send(It.IsAny<ProcessTicketUsedWebhookCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OrganizerWebhook_ValidXOrganizerApiKey_Returns200OK()
    {
        // Arrange
        var (controller, mediatorMock) = CreateOrganizerController();
        controller.HttpContext.Request.Headers["X-Organizer-ApiKey"] = OrganizerValidSecret;
        var payload = new OrganizerTicketUsedWebhookRequest { TicketCode = "TCK-QA-001" };

        // Act
        var result = await controller.HandleTicketUsedWebhook(payload);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);
        mediatorMock.Verify(m => m.Send(It.IsAny<ProcessTicketUsedWebhookCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Bearer " + OrganizerValidSecret)]
    [InlineData("Apikey " + OrganizerValidSecret)]
    public async Task OrganizerWebhook_ValidAuthorizationHeader_Returns200OK(string authHeader)
    {
        // Arrange
        var (controller, mediatorMock) = CreateOrganizerController();
        controller.HttpContext.Request.Headers["Authorization"] = authHeader;
        var payload = new OrganizerTicketUsedWebhookRequest { TicketCode = "TCK-QA-001" };

        // Act
        var result = await controller.HandleTicketUsedWebhook(payload);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);
        mediatorMock.Verify(m => m.Send(It.IsAny<ProcessTicketUsedWebhookCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
