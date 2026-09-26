using MediatR;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.ResaleListings.Commands.ProcessSePayWebhook;

namespace TicketShield.Application.Features.ResaleListings.Commands.SimulatePaymentSuccess;

public record SimulatePaymentSuccessCommand(Guid ListingId) : IRequest<ApiResponse<ProcessSePayWebhookResponse>>;
