using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Organizers.Queries.GetOrganizers;

namespace TicketShield.API.Controllers;

/// <summary>
/// Controller cung cấp thông tin danh mục Ban tổ chức hợp tác
/// </summary>
[Route("api/organizers")]
[Route("api/v1/organizers")]
public class OrganizersController : ApiControllerBase
{
    /// <summary>
    /// Lấy danh sách các Ban tổ chức hợp tác đang hoạt động
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<List<OrganizerDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrganizers(CancellationToken ct = default)
    {
        var result = await Mediator.Send(new GetOrganizersQuery(), ct);
        return Ok(result);
    }
}
