using Microsoft.AspNetCore.Mvc;
using TicketShield.Settlement.API.Payouts;

namespace TicketShield.Settlement.API.Controllers;

[ApiController]
[Route("api/v1/payouts")]
public class PayoutsController : ControllerBase
{
    private readonly SettlementPayoutService _payouts;

    public PayoutsController(SettlementPayoutService payouts)
    {
        _payouts = payouts;
    }

    [HttpPost]
    public async Task<IActionResult> Accept([FromBody] PayoutCommand command, CancellationToken cancellationToken)
    {
        var result = await _payouts.AcceptAsync(command, cancellationToken);
        if (!result.Accepted)
        {
            return BadRequest(new { result.Error });
        }

        return Ok(result);
    }

    [HttpGet("{idempotencyKey}")]
    public async Task<IActionResult> Status(string idempotencyKey, CancellationToken cancellationToken)
    {
        var order = await _payouts.FindAsync(idempotencyKey, cancellationToken);
        if (order == null)
        {
            return NotFound();
        }

        return Ok(new
        {
            order.EscrowId,
            order.IdempotencyKey,
            state = order.State.ToString(),
            order.BankReference,
            order.AttemptCount
        });
    }
}
