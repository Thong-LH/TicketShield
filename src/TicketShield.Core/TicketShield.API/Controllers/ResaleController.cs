using Microsoft.AspNetCore.Mvc;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Resale;

namespace TicketShield.API.Controllers;

/// <summary>
/// Bí danh tương thích ngược cho ResaleController (đã đổi tên thành TicketVerificationsController)
/// </summary>
[NonController]
[Obsolete("Sử dụng TicketVerificationsController thay thế")]
public class ResaleController(
    ITicketVerificationService verificationService,
    ICurrentUserService? currentUserService = null) : TicketVerificationsController(verificationService, currentUserService)
{
}
