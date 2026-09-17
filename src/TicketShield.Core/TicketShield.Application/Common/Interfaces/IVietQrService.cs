using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Common.Interfaces;

public interface IVietQrService
{
    VietQrQuickLinkResult GenerateQuickLink(VietQrQuickLinkRequest request);
    VietQrQuickLinkResult GenerateSystemQuickLink(decimal amount, string transferContent, string? template = null);
}
