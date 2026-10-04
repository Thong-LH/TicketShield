using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using TicketShield.Application.Features.Payouts.Commands.ApplyPayoutReport;

namespace TicketShield.API.Controllers;

[AllowAnonymous]
[Route("api/v1/internal/payouts")]
public class InternalPayoutReportsController : ApiControllerBase
{
    private readonly IConfiguration _configuration;

    public InternalPayoutReportsController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpPost("reports")]
    public async Task<IActionResult> Apply([FromBody] PayoutReportRequest request)
    {
        var expected = _configuration["Settlement:SharedSecret"] ?? "";
        if (!Request.Headers.TryGetValue("X-Settlement-Key", out var provided) || provided != expected)
        {
            return Unauthorized();
        }

        await Mediator.Send(new ApplyPayoutReportCommand(request.EscrowId, request.Succeeded, request.BankReference));
        return Ok();
    }

    public class PayoutReportRequest
    {
        public Guid EscrowId { get; set; }
        public string? IdempotencyKey { get; set; }
        public bool Succeeded { get; set; }
        public string? BankReference { get; set; }
    }
}
