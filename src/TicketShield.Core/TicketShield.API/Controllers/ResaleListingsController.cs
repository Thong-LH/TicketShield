using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketShield.API.Filters;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.ResaleListings.Commands.CreateResaleListing;
using TicketShield.Application.Features.ResaleListings.Queries.GetResaleListingDetail;
using TicketShield.Application.Resale;

namespace TicketShield.API.Controllers;

[Route("api/v1/resale-listings")]
[ResaleErrors]
public class ResaleListingsController(
    ITicketVerificationService? verificationService = null,
    ICurrentUserService? currentUserService = null) : ApiControllerBase
{
    private string Seller => currentUserService?.UserId?.ToString("D")
                          ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                          ?? User.FindFirst("sub")?.Value
                          ?? throw new ResaleWorkflowException("MISSING_SUBJECT", 401);

    /// <summary>
    /// SCRUM-25: Create resale listing with optional private token (US-2.3)
    /// </summary>
    /// <param name="command">Resale listing payload with Price Ceiling and Mode</param>
    /// <returns>Created listing details and private access token if private</returns>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CreateResaleListingResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateListing([FromBody] CreateResaleListingCommand command)
    {
        var result = await Mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Niêm yết vé lên thị trường sau khi đã xác thực và khóa vé thành công
    /// </summary>
    [Authorize]
    [HttpPost("/api/resale-listings")]
    [HttpPost("publish")]
    [ProducesResponseType(typeof(ApiResponse<VerificationResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<VerificationResult>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Publish(
        [FromBody] PublishBody body,
        [FromHeader(Name = "Idempotency-Key")] string key,
        CancellationToken ct)
    {
        if (verificationService == null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResponse<object>.FailureResponse("SERVICE_UNAVAILABLE", ["Verification service is not available."]));

        var result = await verificationService.Publish(Seller, key, body, ct);
        return StatusCode(result.Status.EndsWith("Pending", StringComparison.Ordinal) ? 202 : 200,
            ApiResponse<VerificationResult>.SuccessResponse(result, result.Status));
    }

    /// <summary>
    /// Xem danh sách vé đã xác thực đang niêm yết trên thị trường
    /// </summary>
    [AllowAnonymous]
    [HttpGet]
    [HttpGet("/api/resale-listings")]
    [ProducesResponseType(typeof(ApiResponse<List<ListingResult>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Marketplace(CancellationToken ct, int page = 1, int size = 20)
    {
        if (verificationService == null)
            return Ok(ApiResponse<List<ListingResult>>.SuccessResponse([]));

        return Ok(ApiResponse<List<ListingResult>>.SuccessResponse(await verificationService.Marketplace(page, size, ct)));
    }

    /// <summary>
    /// SCRUM-26: Validate private listing access token and get listing detail (US-2.3)
    /// </summary>
    /// <param name="id">Listing ID</param>
    /// <param name="token">Private access token (required if listing is private)</param>
    /// <returns>Public / Private resale listing preview details</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ResaleListingDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetListingDetail([FromRoute] Guid id, [FromQuery] string? token = null)
    {
        var query = new GetResaleListingDetailQuery(id, token);
        var result = await Mediator.Send(query);
        return Ok(result);
    }
}

