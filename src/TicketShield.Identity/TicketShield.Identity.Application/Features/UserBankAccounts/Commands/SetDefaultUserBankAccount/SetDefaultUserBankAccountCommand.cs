using MediatR;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Application.Features.UserBankAccounts.Dtos;

namespace TicketShield.Identity.Application.Features.UserBankAccounts.Commands.SetDefaultUserBankAccount;

/// <summary>
/// Đặt một tài khoản ngân hàng bất kỳ của user thành tài khoản mặc định (nhận tiền giải ngân).
/// - Hạ IsDefault của tất cả các tài khoản khác xuống false.
/// - Set IsDefault = true cho tài khoản được chỉ định.
/// - Publish event IUserBankAccountLinkedEvent để Core cập nhật snapshot thanh toán.
/// </summary>
public class SetDefaultUserBankAccountCommand : IRequest<ApiResponse<UserBankAccountDto>>
{
    public Guid BankAccountId { get; set; }

    public SetDefaultUserBankAccountCommand() { }

    public SetDefaultUserBankAccountCommand(Guid bankAccountId)
    {
        BankAccountId = bankAccountId;
    }
}
