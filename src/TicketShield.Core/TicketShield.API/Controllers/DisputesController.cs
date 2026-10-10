using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Disputes.Commands.AddDisputeEvidence;
using TicketShield.Application.Features.Disputes.Commands.CreateDispute;
using TicketShield.Application.Features.Disputes.Commands.ResolveDispute;
using TicketShield.Application.Features.Disputes.Queries.GetDisputeEvidenceFile;
using TicketShield.Application.Features.Disputes.Queries.GetDisputeRecommendation;

namespace TicketShield.API.Controllers;

[Authorize]
[Route("api/v1/disputes")]
public class DisputesController : ApiControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CreateDisputeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] CreateDisputeCommand command, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/evidence")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [ProducesResponseType(typeof(ApiResponse<AddDisputeEvidenceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddEvidence(Guid id, IFormFile? file, CancellationToken cancellationToken)
    {
        byte[] content = Array.Empty<byte>();
        if (file != null)
        {
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        var result = await Mediator.Send(new AddDisputeEvidenceCommand
        {
            DisputeId = id,
            Content = content,
            ContentType = file?.ContentType
        }, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/resolution")]
    [Authorize(Roles = "Admin,Cskh")]
    [ProducesResponseType(typeof(ApiResponse<ResolveDisputeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Resolve(Guid id, [FromBody] ResolveDisputeCommand command, CancellationToken cancellationToken)
    {
        command.DisputeId = id;
        var result = await Mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}/recommendation")]
    [Authorize(Roles = "Admin,Cskh")]
    [ProducesResponseType(typeof(ApiResponse<DisputeRecommendationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRecommendation(Guid id, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new GetDisputeRecommendationQuery { DisputeId = id }, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}/evidence/{evidenceId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEvidence(Guid id, Guid evidenceId, CancellationToken cancellationToken)
    {
        var file = await Mediator.Send(new GetDisputeEvidenceFileQuery
        {
            DisputeId = id,
            EvidenceId = evidenceId
        }, cancellationToken);
        return File(file.Content, file.ContentType);
    }
}
