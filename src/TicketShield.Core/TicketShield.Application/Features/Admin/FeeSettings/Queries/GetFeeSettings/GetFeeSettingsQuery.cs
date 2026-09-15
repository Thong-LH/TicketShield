using MediatR;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.FeeSettings.Models;

namespace TicketShield.Application.Features.Admin.FeeSettings.Queries.GetFeeSettings;

public record GetFeeSettingsQuery : IRequest<ApiResponse<ResaleFeeConfig>>;
