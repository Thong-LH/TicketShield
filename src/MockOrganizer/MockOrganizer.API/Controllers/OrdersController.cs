using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MockOrganizer.API.Data;
using MockOrganizer.API.Entities;

namespace MockOrganizer.API.Controllers;

[ApiController]
[Route("api/organizer/orders")]
public class OrdersController(OrganizerDbContext db, ILogger<OrdersController> logger)
    : ControllerBase
{
    private const int MaxRetries = 3;

    /// <summary>
    /// POST /api/organizer/orders/checkout
    /// Lock trừ tồn kho nguyên tử và tạo đơn hàng sơ cấp.
    /// </summary>
    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout(
        [FromBody] CheckoutRequest request, CancellationToken ct)
    {
        if (request.Quantity <= 0)
            return BadRequest(new { message = "Quantity must be greater than 0." });

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                // 1. Đọc tier (tracked để EF detect xmin thay đổi)
                var tier = await db.TicketTiers
                    .FirstOrDefaultAsync(t => t.Id == request.TierId, ct);

                if (tier is null)
                    return NotFound(new { message = $"Tier '{request.TierId}' not found." });

                // 2. Kiểm tra tồn kho
                if (tier.AvailableQuantity < request.Quantity)
                    return Conflict(new
                    {
                        message   = "Not enough inventory.",
                        available = tier.AvailableQuantity,
                        requested = request.Quantity
                    });

                // 3. Trừ tồn kho nguyên tử
                tier.AvailableQuantity -= request.Quantity;

                // 4. Tạo đơn hàng
                var order = new MockOrder
                {
                    Id          = Guid.NewGuid(),
                    TierId      = tier.Id,
                    EventId     = tier.EventId,
                    TierName    = tier.TierName,
                    Quantity    = request.Quantity,
                    UnitPrice   = tier.Price,
                    TotalAmount = tier.Price * request.Quantity,
                    Status      = "PENDING",
                    BuyerEmail  = request.BuyerEmail,
                };
                db.MockOrders.Add(order);

                // 5. SaveChanges — EF kiểm tra xmin, ném DbUpdateConcurrencyException nếu race
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return Ok(new
                {
                    orderId            = order.Id,
                    tierId             = order.TierId,
                    eventId            = order.EventId,
                    tierName           = order.TierName,
                    quantity           = order.Quantity,
                    unitPrice          = order.UnitPrice,
                    totalAmount        = order.TotalAmount,
                    status             = order.Status,
                    remainingAvailable = tier.AvailableQuantity,
                });
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await tx.RollbackAsync(ct);
                logger.LogWarning(
                    "Checkout attempt {Attempt}/{Max} lost concurrency race for tier {TierId}. Retrying...",
                    attempt, MaxRetries, request.TierId);

                if (attempt == MaxRetries)
                {
                    logger.LogError(ex, "Checkout failed after {Max} retries for tier {TierId}.",
                        MaxRetries, request.TierId);
                    return StatusCode(503, new { message = "Server is busy, please retry." });
                }

                // Back-off nhỏ trước khi retry
                await Task.Delay(attempt * 50, ct);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                logger.LogError(ex, "Unexpected error during checkout for tier {TierId}.", request.TierId);
                throw;
            }
        }

        return StatusCode(503, new { message = "Checkout failed unexpectedly." });
    }
}

public class CheckoutRequest
{
    public Guid TierId { get; set; }
    public int Quantity { get; set; }
    public string? BuyerEmail { get; set; }
}
