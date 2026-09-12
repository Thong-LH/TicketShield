using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketShield.API.Filters;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.ResaleListings.Commands.CancelResaleListing;
using TicketShield.Application.Features.ResaleListings.Queries.GetResaleListingByPrivateToken;
using TicketShield.Application.Features.ResaleListings.Queries.GetResaleListingDetail;
using TicketShield.Application.Features.ResaleListings.Queries.GetSellerListings;
using TicketShield.Application.Resale;
using TicketShield.Domain.Enums;

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
    /// Niêm yết vé lên thị trường sau khi đã xác thực và khóa vé thành công (US-2.3)
    /// </summary>
    [Authorize]
    [HttpPost]
    [HttpPost("publish")]
    [HttpPost("/api/resale-listings")]
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

    /// <summary>
    /// SCRUM-30: Get private resale listing detail by private share token
    /// </summary>
    /// <param name="shareToken">Private share token from URL /p/{shareToken}</param>
    /// <returns>Private resale listing preview details</returns>
    [HttpGet("private/{shareToken}")]
    [ProducesResponseType(typeof(ApiResponse<ResaleListingDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPrivateListingDetail([FromRoute] string shareToken)
    {
        var query = new GetResaleListingByPrivateTokenQuery(shareToken);
        var result = await Mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// SCRUM-32: BE-CORE-2.4.1 Get seller listings api (US-2.4)
    /// </summary>
    /// <param name="status">Optional status filter (e.g. Verified, Transacting, Sold, Cancelled)</param>
    /// <returns>List of seller's resale ticket listings</returns>
    [Authorize]
    [HttpGet("my-listings")]
    [ProducesResponseType(typeof(ApiResponse<List<SellerListingDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyListings([FromQuery] ListingStatus? status = null)
    {
        var query = new GetSellerListingsQuery(status);
        var result = await Mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// SCRUM-33: BE-CORE-2.4.2 Cancel resale listing api (US-2.4)
    /// </summary>
    /// <param name="id">Listing ID to cancel</param>
    /// <returns>Cancelled listing details</returns>
    [Authorize]
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(ApiResponse<CancelResaleListingResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelListing([FromRoute] Guid id)
    {
        var command = new CancelResaleListingCommand(id);
        var result = await Mediator.Send(command);
        return Ok(result);
    }
}


