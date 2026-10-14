using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.Organizers.Commands.CreateOrganizer;
using TicketShield.Application.Features.Admin.Organizers.Commands.SyncOrganizerEvents;
using TicketShield.Application.Features.Admin.Organizers.Commands.TestOrganizerConnection;
using TicketShield.Application.Features.Admin.Organizers.Commands.UpdateOrganizerConnection;
using TicketShield.Application.Features.Admin.Organizers.Models;
using TicketShield.Application.Features.Admin.Organizers.Queries.GetAdminOrganizerDetail;
using TicketShield.Application.Features.Admin.Organizers.Queries.GetAdminOrganizers;

namespace TicketShield.API.Controllers.Admin;

/// <summary>
/// Quản trị đối tác Ban tổ chức và Cổng kết nối động (Admin Organizer Partner Management and Gateway)
/// Đáp ứng yêu cầu US-7.5 (SCRUM-239 and BE-CORE-7.5.1)
/// </summary>
[Authorize(Roles = "Admin")]
[Route("api/v1/admin/organizers")]
public class AdminOrganizersController : ApiControllerBase
{
    /// <summary>
    /// Lấy danh sách toàn bộ Ban tổ chức đối tác kèm thông số kết nối
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<AdminOrganizerDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllOrganizers(CancellationToken ct)
    {
        var result = await Mediator.Send(new GetAdminOrganizersQuery(), ct);
        return Ok(result);
    }

    /// <summary>
    /// Lấy thông tin chi tiết cấu hình của một Ban tổ chức
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<AdminOrganizerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrganizerDetail(Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetAdminOrganizerDetailQuery(id), ct);
        return Ok(result);
    }

    /// <summary>
    /// Thêm mới đối tác Ban tổ chức và cấu hình endpoint
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<AdminOrganizerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateOrganizer(
        [FromBody] CreateOrganizerCommand command,
        CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// Cập nhật thông tin và thông số kết nối (API URL, gRPC endpoint, Webhook Secret) của Ban tổ chức
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<AdminOrganizerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateOrganizerConnection(
        Guid id,
        [FromBody] UpdateOrganizerConnectionCommand command,
        CancellationToken ct)
    {
        command.Id = id;
        var result = await Mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// Kiểm tra tính sẵn sàng (Health/Ping probe) kết nối tới endpoint của Ban tổ chức
    /// </summary>
    [HttpPost("{id:guid}/test-connection")]
    [HttpPost("{id:guid}/ping")]
    [ProducesResponseType(typeof(ApiResponse<TestOrganizerConnectionResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TestConnection(Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new TestOrganizerConnectionCommand(id), ct);
        return Ok(result);
    }

    /// <summary>
    /// Đồng bộ danh mục sự kiện và hạng vé từ Ban tổ chức về sàn (Catalog Ingestion)
    /// </summary>
    [HttpPost("{id:guid}/sync-events")]
    [ProducesResponseType(typeof(ApiResponse<SyncOrganizerEventsResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SyncEvents(Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new SyncOrganizerEventsCommand(id), ct);
        return Ok(result);
    }
}
