using Microsoft.AspNetCore.Mvc;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.ResaleListings.Commands.CreateResaleListing;
using TicketShield.Application.Features.ResaleListings.Queries.GetResaleListingDetail;

namespace TicketShield.API.Controllers;

[Route("api/v1/resale-listings")]
public class ResaleListingsController : ApiControllerBase
{
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
