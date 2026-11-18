using MediatR;
using TicketShield.Identity.Application.Common.Models;

namespace TicketShield.Identity.Application.Features.UserBankAccounts.Commands.DeleteUserBankAccount;

/// <summary>
/// Xóa một tài khoản ngân hàng đã liên kết của user hiện tại.
/// - Không cho phép xóa nếu đây là tài khoản Default duy nhất còn lại.
/// - Nếu xóa tài khoản Default khi còn tài khoản khác, tự động nâng tài khoản
///   cũ nhất (CreatedAt sớm nhất) thành Default và publish event cập nhật payout.
/// </summary>
public class DeleteUserBankAccountCommand : IRequest<ApiResponse<object>>
{
    public Guid BankAccountId { get; set; }

    public DeleteUserBankAccountCommand() { }

    public DeleteUserBankAccountCommand(Guid bankAccountId)
    {
        BankAccountId = bankAccountId;
    }
}
